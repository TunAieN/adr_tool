# V2.1.46 – First-run feed coachmark handshake

## Root cause

The TikTok navigation bar may appear in Appium XML while the first-run graphical "Swipe up for more" tutorial still covers the video. Before V2.1.46, onboarding completion only waited for visible navigation controls. Preflight attempted the swipe only if the coachmark label was exposed to UiAutomator or a Profile tap had already failed.

## Change

- If gender/interests setup has just finished and the actual TikTok Home/For You navigation is present, perform exactly one bounded upward swipe **before** logging onboarding completion. This covers coachmarks that are visual-only and not exposed in XML.
- Require TikTok foreground package and Home + Profile + For You labels. Do not swipe in Google/login, Profile, edit profile, LIVE room or unexpected TikTok pages.
- Detect explicit tutorial text even when its overlay hides the navigation nodes.
- Read another XML snapshot after the gesture and log evidence; do not claim that a visual-only overlay is definitively absent based on XML.
- Preserve Profile recovery, old selectors, account vault and all prior phases.

## New logs

- `NEW_DEVICE_FEED_TUTORIAL_SWIPE_REQUESTED: method=SwipeUp; coachmark=possible_graphic_overlay_after_onboarding; oneSwipeOnly=true`
- `NEW_DEVICE_FEED_TUTORIAL_SWIPE_CHECK: overlay_not_detected_in_XML; currentFeed=True; gestureCompleted=true`
- `NEW_DEVICE_ONBOARDING_COMPLETE`

These are *expected* logs; they are not device-test evidence. Run BUILD_RUN.bat on Windows; inspect live device behavior. If the screenshot still shows the tutorial, capture `adb shell uiautomator dump` and the tool logs before proceeding.
