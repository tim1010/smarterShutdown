using System.Runtime.Versioning;

namespace SmarterShutdown.Core.Teams;

// PLACEHOLDER. Returns false (not in a call) until the audio-session enumeration is implemented.
//
// Real implementation plan:
//   1. Use MMDeviceEnumerator (IMMDeviceEnumerator) to get the default eCapture endpoint.
//   2. Activate IAudioSessionManager2 on the endpoint.
//   3. Enumerate IAudioSessionEnumerator → IAudioSessionControl2 entries.
//   4. For each session, check ProcessId, look up the process name, match against
//      { "ms-teams", "ms-teamsforwork", "Teams", "MSTeams" } case-insensitively.
//   5. Return true if any matching session is in AudioSessionStateActive.
//
// The detector runs in the Notifier process (user session), not the SYSTEM service —
// SYSTEM cannot enumerate user-session audio sessions.
//
// Returning false is the safe default: the scheduler will proceed normally instead of
// indefinitely deferring shutdowns due to a false-positive call detection.
[SupportedOSPlatform("windows")]
public sealed class Win32TeamsDetector : ITeamsDetector
{
    public bool IsInCall() => false;
}
