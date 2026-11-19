// Body tracking for iOS: front camera -> Apple Vision body pose -> 13 joints polled by Unity.
#import <AVFoundation/AVFoundation.h>
#import <Vision/Vision.h>
#import <UIKit/UIKit.h>

static const int kJoints = 13;
static float gLatest[1 + kJoints * 3];
static NSLock *gLock;
static int gState = 0; // 0 idle, 1 running, -1 denied/failed
// Orientation auto-detect: the front camera buffer's rotation differs by device and by how the phone is held,
// so we cycle through the 4 rotations until Vision finds a person, then stay on that one.
static const CGImagePropertyOrientation kOrients[4] = {kCGImagePropertyOrientationUp, kCGImagePropertyOrientationRight, kCGImagePropertyOrientationDown, kCGImagePropertyOrientationLeft};
static int gOrient = 0, gMiss = 0, gDetections = 0;

@interface PBCapture : NSObject <AVCaptureVideoDataOutputSampleBufferDelegate>
@property(nonatomic, strong) AVCaptureSession *session;
@property(nonatomic, strong) VNDetectHumanBodyPoseRequest *request;
@property(nonatomic, strong) dispatch_queue_t queue;
@property(nonatomic) AVCaptureVideoOrientation orientation;
@end

@implementation PBCapture
- (void)start {
  self.queue = dispatch_queue_create("posebridge", DISPATCH_QUEUE_SERIAL);
  self.request = [[VNDetectHumanBodyPoseRequest alloc] init];
  self.orientation = [PBCapture currentVideoOrientation];
  AVAuthorizationStatus st = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
  if (st == AVAuthorizationStatusDenied || st == AVAuthorizationStatusRestricted) { gState = -1; return; }
  [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo completionHandler:^(BOOL granted) {
    if (!granted) { gState = -1; return; }
    dispatch_async(self.queue, ^{ [self configure]; });
  }];
}

+ (AVCaptureVideoOrientation)currentVideoOrientation {
  __block UIInterfaceOrientation o = UIInterfaceOrientationLandscapeLeft;
  void (^read)(void) = ^{
    for (UIScene *s in UIApplication.sharedApplication.connectedScenes)
      if ([s isKindOfClass:UIWindowScene.class]) { o = ((UIWindowScene *)s).interfaceOrientation; break; }
  };
  if (NSThread.isMainThread) read(); else dispatch_sync(dispatch_get_main_queue(), read);
  switch (o) {
    case UIInterfaceOrientationLandscapeRight: return AVCaptureVideoOrientationLandscapeRight;
    case UIInterfaceOrientationPortrait: return AVCaptureVideoOrientationPortrait;
    case UIInterfaceOrientationPortraitUpsideDown: return AVCaptureVideoOrientationPortraitUpsideDown;
    default: return AVCaptureVideoOrientationLandscapeLeft;
  }
}

- (void)configure {
  AVCaptureDevice *cam = [AVCaptureDevice defaultDeviceWithDeviceType:AVCaptureDeviceTypeBuiltInWideAngleCamera
                                                            mediaType:AVMediaTypeVideo
                                                             position:AVCaptureDevicePositionFront];
  if (!cam) { gState = -1; return; }
  NSError *err = nil;
  AVCaptureDeviceInput *input = [AVCaptureDeviceInput deviceInputWithDevice:cam error:&err];
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
  if (c.isVideoOrientationSupported) c.videoOrientation = AVCaptureVideoOrientationPortrait; // known base; Vision gets the rotation
  if (c.isVideoMirroringSupported) { c.automaticallyAdjustsVideoMirroring = NO; c.videoMirrored = NO; }
  // first guess from how the app is held: landscape needs a quarter turn
  switch (self.orientation) {
    case AVCaptureVideoOrientationLandscapeLeft: gOrient = 1; break;
    case AVCaptureVideoOrientationLandscapeRight: gOrient = 3; break;
    case AVCaptureVideoOrientationPortraitUpsideDown: gOrient = 2; break;
    default: gOrient = 0; break;
  }
  if ([cam lockForConfiguration:nil]) { cam.activeVideoMinFrameDuration = CMTimeMake(1, 30); [cam unlockForConfiguration]; }
  self.session = s;
  [s startRunning];
  gState = 1;
}

- (void)stop { [self.session stopRunning]; self.session = nil; gState = 0; }

- (void)captureOutput:(AVCaptureOutput *)output didOutputSampleBuffer:(CMSampleBufferRef)sb fromConnection:(AVCaptureConnection *)conn {
  CVPixelBufferRef px = CMSampleBufferGetImageBuffer(sb);
  if (!px) return;
  VNImageRequestHandler *h = [[VNImageRequestHandler alloc] initWithCVPixelBuffer:px orientation:kOrients[gOrient] options:@{}];
  if (![h performRequests:@[ self.request ] error:nil]) return;
  VNHumanBodyPoseObservation *obs = (VNHumanBodyPoseObservation *)self.request.results.firstObject;
  if (obs) { gMiss = 0; gDetections++; }
  else if (++gMiss >= 12) { gMiss = 0; gOrient = (gOrient + 1) % 4; } // try the next rotation
  float buf[1 + kJoints * 3] = {0};
  buf[0] = (float)CACurrentMediaTime();
  if (obs) {
    VNHumanBodyPoseObservationJointName names[kJoints] = {
      VNHumanBodyPoseObservationJointNameNose,
      VNHumanBodyPoseObservationJointNameLeftShoulder, VNHumanBodyPoseObservationJointNameRightShoulder,
      VNHumanBodyPoseObservationJointNameLeftElbow, VNHumanBodyPoseObservationJointNameRightElbow,
      VNHumanBodyPoseObservationJointNameLeftWrist, VNHumanBodyPoseObservationJointNameRightWrist,
      VNHumanBodyPoseObservationJointNameLeftHip, VNHumanBodyPoseObservationJointNameRightHip,
      VNHumanBodyPoseObservationJointNameLeftKnee, VNHumanBodyPoseObservationJointNameRightKnee,
      VNHumanBodyPoseObservationJointNameLeftAnkle, VNHumanBodyPoseObservationJointNameRightAnkle};
    for (int i = 0; i < kJoints; i++) {
      VNRecognizedPoint *p = [obs recognizedPointForJointName:names[i] error:nil];
      if (p && p.confidence > 0.1) {
        buf[1 + i * 3] = (float)p.location.x;
        buf[2 + i * 3] = 1.0f - (float)p.location.y; // Vision is bottom-left origin; Unity side expects top-left
        buf[3 + i * 3] = p.confidence;
      }
    }
  }
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
void PB_Stop() { PBCapture *c = gCapture; gCapture = nil; if (c) dispatch_async(c.queue, ^{ [c stop]; }); }
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
