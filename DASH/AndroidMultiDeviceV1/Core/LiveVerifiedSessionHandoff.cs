namespace AndroidMultiDeviceV1.Core;

/// <summary>
/// Bridges a verified login/profile session into the existing Phase 1-5 LIVE
/// pipeline. It never creates or owns another Appium session.
/// </summary>
public static class LiveVerifiedSessionHandoff
{
    public static async Task RunAsync(AppiumSession session,LiveManagerSettings config,
        AdbClient adb,string deviceSerial,Action<string> log,CancellationToken ct,
        Action? onIdleOneSecondVerified=null,GlobalRoomSlotManager? roomSlots=null,
        int maxDevicesPerRoom=5,string? slotDeviceKey=null,
        Func<int>? roomDeviceLimitProvider=null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(config);
        if(!config.KeywordSearchEnabled)
            throw new InvalidOperationException(
                "AUTO_LIVE_TRANSITION_BLOCKED: Tìm LIVE bằng từ khóa đang tắt trong Cấu hình LIVE.");
        var errors=config.Validate();
        if(errors.Count>0)
            throw new InvalidDataException("AUTO_LIVE_TRANSITION_CONFIG_INVALID: "+string.Join("; ",errors));

        var keywordQueue=new LiveKeywordLruQueue(config.Keywords);
        LiveSearchIdleSafety? idleScope=null;
        try
        {
            idleScope=await LiveSearchIdleSafety.EnableAsync(session,log,ct);
            // EnableAsync performs a server-side read-back and rejects any value
            // other than 1000ms before this callback can expose SEARCH_GRID.
            onIdleOneSecondVerified?.Invoke();
            void EngineLog(string message)
            {
                idleScope.RecordEvidence(message);
                log(message);
            }

            var searchCycle=new LiveSearchCycle(session,config,EngineLog,
                card=>EngineLog($"LIVE_CARD: @{card.Creator}; viewers={card.Viewers?.ToString()??"?"}"),
                adb,deviceSerial,keywordQueue);
            LiveSearchReport report=await searchCycle.FindAcceptedRoomAsync(ct);

            bool valid=await idleScope.ValidateAcceptedRoomAsync(config,ct);
            if(!valid)
                throw new InvalidOperationException(
                    "AUTO_LIVE_TRANSITION_ROOM_INVALID: Phase 3 accepted room did not pass handoff validation.");
            await idleScope.VerifyPhase5IdleAsync(ct);
            EngineLog("SAFE_IDLE_FLOW_HANDOFF: verified account auto-transitioned; " +
                "idle kept at 1000ms for Phase 5 optimization.");

            var drafts=config.CommentDrafts.Where(value=>!string.IsNullOrWhiteSpace(value))
                .Select(value=>value.Trim()).ToList();
            EngineLog($"AUTO_LIVE_ROOM_ACCEPTED: {report.SelectedCandidate?.Title??"LIVE"}; " +
                "handoff=CONTINUOUS_COORDINATOR.");
            var coordinator=new LiveRoomContinuousCoordinator(session,config,drafts,EngineLog,
                _=>{},adb,deviceSerial,searchCycle.FindAcceptedRoomAsync,
                roomSlots:roomSlots,slotDeviceKey:slotDeviceKey,
                maxDevicesPerRoom:maxDevicesPerRoom,
                roomDeviceLimitProvider:roomDeviceLimitProvider);
            await coordinator.RunAsync(ct);
            throw new InvalidOperationException(
                "Continuous coordinator returned without user cancellation.");
        }
        finally
        {
            if(idleScope is not null)
            {
                log("SAFE_IDLE_TEARDOWN_START: restoring original Appium idle setting.");
                await idleScope.RestoreAsync();
            }
        }
    }
}
