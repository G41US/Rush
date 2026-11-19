// Body tracking for iOS: front camera -> Google MediaPipe Pose Landmarker (same model as the web version)
// -> 13 joints polled by Unity.
#import <AVFoundation/AVFoundation.h>
#import <UIKit/UIKit.h>
#import <MediaPipeTasksVision/MediaPipeTasksVision.h>

static const int kJoints = 13;
// MediaPipe's 33-point indices for: nose, L/R shoulder, L/R elbow, L/R wrist, L/R hip, L/R knee, L/R ankle
static const int kIdx[kJoints] = {0, 11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28};
static float gLatest[1 + kJoints * 3];
static NSLock *gLock;
static int gState = 0; // 0 idle, 1 running, -1 denied/failed
// Insurance: if the picture reaches the model rotated, cycle quarter turns until most joints are seen.
static const UIImageOrientation kOrients[4] = {UIImageOrientationUp, UIImageOrientationRight, UIImageOrientationDown, UIImageOrientationLeft};
static int gOrient = 0, gMiss = 0, gDetections = 0;

@interface PBCapture : NSObject <AVCaptureVideoDataOutputSampleBufferDelegate>
@property(nonatomic, strong) AVCaptureSession *session;
@property(nonatomic, strong) MPPPoseLandmarker *landmarker;
@property(nonatomic, strong) dispatch_queue_t queue;
@property(nonatomic) NSInteger lastTs;
@end

@implementation PBCapture
- (void)start {
  self.queue = dispatch_queue_create("posebridge", DISPATCH_QUEUE_SERIAL);
  AVAuthorizationStatus st = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
  if (st == AVAuthorizationStatusDenied || st == AVAuthorizationStatusRestricted) { gState = -1; return; }
  [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo completionHandler:^(BOOL granted) {
    if (!granted) { gState = -1; return; }
    dispatch_async(self.queue, ^{ [self configure]; });
  }];
}

- (BOOL)makeLandmarker {
  NSString *path = [[NSBundle mainBundle] pathForResource:@"pose_landmarker_lite" ofType:@"task" inDirectory:@"Data/Raw"];
  if (!path) { NSLog(@"[PoseBridge] model not found in bundle"); return NO; }
  MPPPoseLandmarkerOptions *o = [[MPPPoseLandmarkerOptions alloc] init];
  o.baseOptions.modelAssetPath = path;
  o.runningMode = MPPRunningModeVideo;
  o.numPoses = 1;
  o.minPoseDetectionConfidence = 0.4f;
  o.minPosePresenceConfidence = 0.4f;
  o.minTrackingConfidence = 0.4f;
  NSError *err = nil;
  self.landmarker = [[MPPPoseLandmarker alloc] initWithOptions:o error:&err];
  if (!self.landmarker) NSLog(@"[PoseBridge] landmarker error %@", err);
  return self.landmarker != nil;
}

- (void)configure {
  if (![self makeLandmarker]) { gState = -1; return; }
  AVCaptureDevice *cam = [AVCaptureDevice defaultDeviceWithDeviceType:AVCaptureDeviceTypeBuiltInWideAngleCamera
                                                            mediaType:AVMediaTypeVideo
                                                             position:AVCaptureDevicePositionFront];
  if (!cam) { gState = -1; return; }
  AVCaptureDeviceInput *input = [AVCaptureDeviceInput deviceInputWithDevice:cam error:nil];
  if (!input) { gState = -1; return; }
  AVCaptureSession *s = [[AVCaptureSession alloc] init];
  s.sessionPreset = AVCaptureSessionPreset640x480;
  if ([s canAddInput:input]) [s addInput:input];
  AVCaptureVideoDataOutput *out = [[AVCaptureVideoDataOutput alloc] init];
  out.alwaysDiscardsLateVideoFrames = YES;
  out.videoSettings = @{(id)kCVPixelBufferPixelFormatTypeKey : @(kCVPixelFormatType_32BGRA)};
  [out setSampleBufferDelegate:self queue:self.queue];
  if ([s canAddOutput:out]) [s addOutput:out];
  AVCaptureConnection *c = [out connectionWithMediaType:AVMediaTypeVideo];
  if (c.isVideoOrientationSupported) c.videoOrientation = AVCaptureVideoOrientationPortrait; // app is portrait
  if (c.isVideoMirroringSupported) { c.automaticallyAdjustsVideoMirroring = NO; c.videoMirrored = NO; }
  if ([cam lockForConfiguration:nil]) { cam.activeVideoMinFrameDuration = CMTimeMake(1, 30); [cam unlockForConfiguration]; }
  self.session = s;
  [s startRunning];
  gState = 1;
}

- (void)stop { [self.session stopRunning]; self.session = nil; self.landmarker = nil; gState = 0; }

- (void)captureOutput:(AVCaptureOutput *)output didOutputSampleBuffer:(CMSampleBufferRef)sb fromConnection:(AVCaptureConnection *)conn {
  if (!self.landmarker) return;
  NSError *err = nil;
  MPPImage *img = [[MPPImage alloc] initWithSampleBuffer:sb orientation:kOrients[gOrient] error:&err];
  if (!img) return;
  NSInteger ts = (NSInteger)(CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(sb)) * 1000);
  if (ts <= self.lastTs) ts = self.lastTs + 1;
  self.lastTs = ts;
  MPPPoseLandmarkerResult *res = [self.landmarker detectVideoFrame:img timestampInMilliseconds:ts error:&err];
  float buf[1 + kJoints * 3] = {0};
  buf[0] = (float)CACurrentMediaTime();
  int good = 0;
  if (res.landmarks.count > 0) {
    NSArray<MPPNormalizedLandmark *> *lm = res.landmarks[0];
    gDetections++;
    for (int i = 0; i < kJoints; i++) {
      if (kIdx[i] >= (int)lm.count) continue;
      MPPNormalizedLandmark *p = lm[kIdx[i]];
      float vis = p.visibility ? p.visibility.floatValue : 1.0f;
      buf[1 + i * 3] = p.x;   // normalized, origin top-left
      buf[2 + i * 3] = p.y;
      buf[3 + i * 3] = vis;
      if (vis > 0.3f && p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1) good++;
    }
  }
  if (good >= 7) gMiss = 0;
  else if (++gMiss >= 20) { gMiss = 0; gOrient = (gOrient + 1) % 4; }
  [gLock lock];
  memcpy(gLatest, buf, sizeof(buf));
  [gLock unlock];
}
@end

static PBCapture *gCapture;

extern "C" {
void PB_Start() {
  if (!gLock) gLock = [[NSLock alloc] init];
  if (gCapture && gState == 1) return;
  gState = 0;
  dispatch_async(dispatch_get_main_queue(), ^{ gCapture = [[PBCapture alloc] init]; [gCapture start]; });
}
void PB_Stop() { PBCapture *c = gCapture; gCapture = nil; if (c && c.queue) dispatch_async(c.queue, ^{ [c stop]; }); }
int PB_State() { return gState; }
int PB_Info() { return gOrient * 1000000 + (gDetections % 1000000); }
int PB_GetLatest(float *outBuf, int len) {
  if (!gLock || len < 1 + kJoints * 3) return 0;
  [gLock lock];
  memcpy(outBuf, gLatest, sizeof(gLatest));
  [gLock unlock];
  return 1 + kJoints * 3;
}
}
