package com.marvelous.posebridge;

import android.app.Activity;
import android.util.Size;
import android.view.Surface;
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
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.pose.Pose;
import com.google.mlkit.vision.pose.PoseDetection;
import com.google.mlkit.vision.pose.PoseDetector;
import com.google.mlkit.vision.pose.PoseLandmark;
import com.google.mlkit.vision.pose.defaults.PoseDetectorOptions;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Front camera -> ML Kit pose (stream mode) -> 13 joints, polled by Unity via getLatest(). */
public class PoseBridge {
    private static final int J = 13;
    private static final int[] IDS = {
        PoseLandmark.NOSE,
        PoseLandmark.LEFT_SHOULDER, PoseLandmark.RIGHT_SHOULDER,
        PoseLandmark.LEFT_ELBOW, PoseLandmark.RIGHT_ELBOW,
        PoseLandmark.LEFT_WRIST, PoseLandmark.RIGHT_WRIST,
        PoseLandmark.LEFT_HIP, PoseLandmark.RIGHT_HIP,
        PoseLandmark.LEFT_KNEE, PoseLandmark.RIGHT_KNEE,
        PoseLandmark.LEFT_ANKLE, PoseLandmark.RIGHT_ANKLE};

    private static final float[] latest = new float[1 + J * 3];
    private static volatile int state = 0; // 0 idle, 1 running, -1 failed
    private static PoseDetector detector;
    private static ExecutorService exec;
    private static ProcessCameraProvider provider;
    private static Owner owner;

    private static class Owner implements LifecycleOwner {
        final LifecycleRegistry reg = new LifecycleRegistry(this);
        @NonNull @Override public Lifecycle getLifecycle() { return reg; }
    }

    public static int getState() { return state; }

    public static float[] getLatest() {
        synchronized (latest) { return latest.clone(); }
    }

    public static void start(final Activity activity) {
        if (state == 1) return;
        activity.runOnUiThread(() -> {
            try {
                detector = PoseDetection.getClient(new PoseDetectorOptions.Builder()
                        .setDetectorMode(PoseDetectorOptions.STREAM_MODE).build());
                exec = Executors.newSingleThreadExecutor();
                owner = new Owner();
                owner.reg.setCurrentState(Lifecycle.State.RESUMED);
                ListenableFuture<ProcessCameraProvider> f = ProcessCameraProvider.getInstance(activity);
                f.addListener(() -> {
                    try {
                        provider = f.get();
                        int rot = activity.getWindowManager().getDefaultDisplay().getRotation();
                        ImageAnalysis analysis = new ImageAnalysis.Builder()
                                .setTargetResolution(new Size(640, 480))
                                .setTargetRotation(rot)
                                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                                .build();
                        analysis.setAnalyzer(exec, PoseBridge::analyze);
                        provider.unbindAll();
                        provider.bindToLifecycle(owner, CameraSelector.DEFAULT_FRONT_CAMERA, analysis);
                        state = 1;
                    } catch (Exception e) { state = -1; }
                }, ContextCompat.getMainExecutor(activity));
            } catch (Exception e) { state = -1; }
        });
    }

    public static void stop(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (provider != null) provider.unbindAll();
            if (owner != null) owner.reg.setCurrentState(Lifecycle.State.DESTROYED);
            state = 0;
        });
    }

    @androidx.camera.core.ExperimentalGetImage
    private static void analyze(ImageProxy proxy) {
        if (proxy.getImage() == null) { proxy.close(); return; }
        final int rotation = proxy.getImageInfo().getRotationDegrees();
        final boolean swap = rotation == 90 || rotation == 270;
        final float w = swap ? proxy.getHeight() : proxy.getWidth();
        final float h = swap ? proxy.getWidth() : proxy.getHeight();
        InputImage img = InputImage.fromMediaImage(proxy.getImage(), rotation);
        detector.process(img)
            .addOnSuccessListener(pose -> publish(pose, w, h))
            .addOnCompleteListener(t -> proxy.close());
    }

    private static void publish(Pose pose, float w, float h) {
        float[] buf = new float[1 + J * 3];
        buf[0] = System.nanoTime() / 1e9f;
        for (int i = 0; i < J; i++) {
            PoseLandmark lm = pose.getPoseLandmark(IDS[i]);
            if (lm != null) {
                buf[1 + i * 3] = lm.getPosition().x / w;
                buf[2 + i * 3] = lm.getPosition().y / h;
                buf[3 + i * 3] = lm.getInFrameLikelihood();
            }
        }
        synchronized (latest) { System.arraycopy(buf, 0, latest, 0, buf.length); }
    }
}
