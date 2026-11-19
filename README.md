# Rail Rush Workout
Body-controlled 3D runner workout. Run in place, lean to switch lanes, jump, squat to duck.

## Builds (GitHub Actions)
- **Android:** Actions → Build APK → `rail-rush-apk`.
- **iPhone:** Actions → Build IPA → `rail-rush-ipa`. It's unsigned: install with Sideloadly or AltStore using your Apple ID (free ID = re-sign every 7 days).
- **Web:** host `www/` on GitHub Pages (HTTPS needed for camera).

## Cast to any TV
In-app **Cast to TV** button. Android opens cast settings (Smart View / Miracast / Chromecast); iPhone uses Screen Mirroring (AirPlay TVs/Apple TV). Non-smart TV: Chromecast/Miracast dongle or USB-C→HDMI.

## Unity version (unity/)
Native rebuild: Unity 6, body tracking via Apple Vision (iOS) and ML Kit (Android).
Workflow "Unity build (APK + IPA)" needs repo secrets UNITY_LICENSE, UNITY_EMAIL, UNITY_PASSWORD.
