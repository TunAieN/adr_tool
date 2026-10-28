from pathlib import Path
root=Path(__file__).resolve().parent/'AndroidMultiDeviceV1'
get=lambda file:(root/file).read_text(encoding='utf-8-sig')
src=get('Core/TikTokChromeLiveSource.cs')
settings=get('Core/LiveManagerSettings.cs')
ui=get('LiveSettingsDialog.cs')
engine=get('Core/LiveSearchEngine.cs')
flow=get('Core/LiveAutonomousWorkflow.cs')
checks={
 'default_android_not_browser': 'LiveSourceMode { get; set; } = "DeviceTikTokSearch"' in settings,
 'cdp_opt_in_ui': 'TikTok Web — Chrome profile CDP riêng' in ui and 'ChromeTikTokWeb' in ui,
 'setting_persisted': 'LiveSourceMode = _sourceMode.SelectedIndex' in ui,
 'chrome_profile_is_dedicated':'chrome-live-cdp-profile' in src and 'remote-debugging-address=127.0.0.1' in src,
 'dynamic_debugging_port':'remote-debugging-port=0' in src,
 'chrome_does_not_choose_normal_window':'ChromeWindowTitleContains' not in src,
 'browser_serialized':'BrowserGate.WaitAsync(ct)' in src,
 'viewer_svg_gate':'viewerSvgAccepted' in src.lower() or 'ViewerSvgAccepted' in src,
 'like_svg_rejected':'likeSvgRejected' in src or 'LikeSvgRejected' in src,
 'web_username_live_link':'/@' in src and '/live' in src,
 'web_candidates_ranked':'OrderByDescending(c=>c.Viewers)' in src,
 'cdp_dispatch_to_android':'candidate.Username,allowGridRefresh:false' in flow,
 'targeted_android_single_card':'TARGETED_FIRST_CARD_PICK' in engine and 'RunTargetedFirstCardAsync' in engine,
 'android_host_exact_match':'PHASE3 LIVE_HOST_MISMATCH' in engine and 'expectedHostUsername' in engine,
 'no_blind_tap_if_lost_ui':'CHROME_CDP_ANDROID_CHECKPOINT_UNVERIFIED' in engine,
 'retain_find_people':'TikTokNavigationRecovery.IsFindPeopleScreen' in engine,
 'retain_original_live':'FindAndOpenSelectedAsync' in engine and 'LIVE_SEARCH_FLOW_SUCCESS' in engine,
 'other_live_workflow_untouched':'LIVE_OWNER_VERIFY' not in flow or 'GLOBAL_SESSION_CREATE' in flow,
}
for k,v in checks.items():print(('PASS' if v else 'FAIL')+': '+k)
assert all(checks.values()), 'CDP integration invariant failed'
print('CDP V5.9.2 STATIC CONTRACT PASS; Windows .NET SDK compile still required')
