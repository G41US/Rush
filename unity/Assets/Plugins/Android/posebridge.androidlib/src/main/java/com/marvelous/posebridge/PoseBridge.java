package com.marvelous.posebridge;

import android.app.Activity;
import android.graphics.Bitmap;
import android.graphics.Matrix;
import android.util.Size;
import androidx.annotation.NonNull;
import androidx.camera.core.CameraSelector;
import androidx.camera.core.ImageAnalysis;
import androidx.camera.core.ImageProxy;
import androidx.camera.lifecycle.ProcessCameraProvider;
import androidx.core.content.ContextCompat;
import androidx.lifecycle.Lifecycle;
import androidx.lifecycle.LifecycleOwner;
import androidx.lifecycle.LifecycleRegistry;
import com.google.common.util.concurrent.ListenableFuture;
import com.google.mediapipe.framework.image.BitmapImageBuilder;
import com.google.mediapipe.framework.image.MPImage;
import com.google.mediapipe.tasks.components.containers.NormalizedLandmark;
import com.google.mediapipe.tasks.core.BaseOptions;
import com.google.mediapipe.tasks.vision.core.RunningMode;
import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarker;
import com.google.mediapipe.tasks.vision.poselandmarker.PoseLandmarkerResult;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Front camera -> Google MediaPipe Pose Landmarker (same model as the web version) -> 13 joints for Unity. */
public class PoseBridge {
    private static final int J = 13;
    // MediaPipe 33-point indices: nose, L/R shoulder, L/R elbow, L/R wrist, L/R hip, L/R knee, L/R ankle
    private static final int[] IDX = {0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28};

    private static final float[] latest = new float[1 + J * 3];
    private static volatile int state = 0; // 0 idle, 1 running, -1 failed
    private static volatile int detections = 0;
    private static PoseLandmarker landmarker;
    private static ExecutorService exec;
    private static ProcessCameraProvider provider;
    private static Owner owner;
    private static long lastTs = 0;

    private static class Owner implements LifecycleOwner {
        final LifecycleRegistry reg = new LifecycleRegistry(this);
        @NonNull @Override public Lifecycle getLifecycle() { return reg; }
    }

    public static int getState() { return state; }
    public static int getDetections() { return detections; }
    public static float[] getLatest() { synchronized (latest) { return latest.clone(); } }

    private static ByteBuffer loadModel(Activity a) throws Exception {
        try (InputStream in = a.getAssets().open("pose_landmarker_lite.task")) {
            ByteArrayOutputStream bo = new ByteArrayOutputStream();
            byte[] b = new byte[1 << 16]; int n;
            while ((n = in.read(b)) > 0) bo.write(b, 0, n);
            byte[] all = bo.toByteArray();
            ByteBuffer bb = ByteBuffer.allocateDirect(all.length).order(ByteOrder.nativeOrder());
            bb.put(all); bb.rewind();
            return bb;
        }
    }

    public static void start(final Activity activity) {
        if (state == 1) return;
        exec = Executors.newSingleThreadExecutor();
        exec.execute(() -> {
            try {
                PoseLandmarker.PoseLandmarkerOptions opts = PoseLandmarker.PoseLandmarkerOptions.builder()
                        .setBaseOptions(BaseOptions.builder().setModelAssetBuffer(loadModel(activity)).build())
                        .setRunningMode(RunningMode.VIDEO)
                        .setNumPoses(1)
                        .setMinPoseDetectionConfidence(0.4f)
                        .setMinPosePresenceConfidence(0.4f)
                        .setMinTrackingConfidence(0.4f)
                        .build();
                landmarker = PoseLandmarker.createFromOptions(activity, opts);
            } catch (Exception e) { state = -1; return; }
            activity.runOnUiThread(() -> bindCamera(activity));
        });
    }

    private static void bindCamera(final Activity activity) {
        try {
            owner = new Owner();
            owner.reg.handleLifecycleEvent(Lifecycle.Event.ON_CREATE); owner.reg.handleLifecycleEvent(Lifecycle.Event.ON_START); owner.reg.handleLifecycleEvent(Lifecycle.Event.ON_RESUME);
            ListenableFuture<ProcessCameraProvider> f = ProcessCameraProvider.getInstance(activity);
            f.addListener(() -> {
                try {
                    provider = f.get();
                    int rot = activity.getWindowManager().getDefaultDisplay().getRotation();
                    ImageAnalysis analysis = new ImageAnalysis.Builder()
                            .setTargetResolution(new Size(480, 640))
                            .setTargetRotation(rot)
                            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                            .setOutputImageFormat(ImageAnalysis.OUTPUT_IMAGE_FORMAT_RGBA_8888)
                            .build();
                    analysis.setAnalyzer(exec, PoseBridge::analyze);
                    provider.unbindAll();
                    provider.bindToLifecycle(owner, CameraSelector.DEFAULT_FRONT_CAMERA, analysis);
                    state = 1;
                } catch (Exception e) { state = -1; }
            }, ContextCompat.getMainExecutor(activity));
        } catch (Exception e) { state = -1; }
    }

    public static void stop(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (provider != null) provider.unbindAll();
            if (owner != null) owner.reg.handleLifecycleEvent(Lifecycle.Event.ON_DESTROY);
            state = 0;
        });
    }

    private static void analyze(ImageProxy proxy) {
        try {
            Bitmap raw = proxy.toBitmap();
            int rotation = proxy.getImageInfo().getRotationDegrees();
            Bitmap up = raw;
            if (rotation != 0) {
                Matrix m = new Matrix(); m.postRotate(rotation);
                up = Bitmap.createBitmap(raw, 0, 0, raw.getWidth(), raw.getHeight(), m, true);
            }
            MPImage img = new BitmapImageBuilder(up).build();
            long ts = System.currentTimeMillis();
            if (ts <= lastTs) ts = lastTs + 1;
            lastTs = ts;
            PoseLandmarkerResult res = landmarker.detectForVideo(img, ts);
            publish(res);
        } catch (Exception ignored) {
        } finally { proxy.close(); }
    }

    private static void publish(PoseLandmarkerResult res) {
        float[] buf = new float[1 + J * 3];
        buf[0] = System.nanoTime() / 1e9f;
        List<List<NormalizedLandmark>> all = res.landmarks();
        if (all != null && !all.isEmpty()) {
            detections++;
            List<NormalizedLandmark> lm = all.get(0);
            for (int i = 0; i < J; i++) {
                if (IDX[i] >= lm.size()) continue;
                NormalizedLandmark p = lm.get(IDX[i]);
                buf[1 + i * 3] = p.x();
                buf[2 + i * 3] = p.y();
                buf[3 + i * 3] = p.visibility().isPresent() ? p.visibility().get() : 1f;
            }
        }
        synchronized (latest) { System.arraycopy(buf, 0, latest, 0, buf.length); }
    }
}
