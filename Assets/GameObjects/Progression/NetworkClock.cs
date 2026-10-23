using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

// Internet time for the challenge rounds (Docs/Progression.md "Challenge-Uhr"): the HTTP "Date" header of a well-known
// server gives the offset of the PC clock once per session. Without network the PC clock is used; MetaProgression
// additionally forgets a stored clock that lies in the future, so a wrong PC clock never freezes the rounds.
public static class NetworkClock
{
    static readonly string[] Hosts = { "https://www.google.com", "https://www.cloudflare.com", "https://www.microsoft.com" };
    const float RetrySeconds = 300f;
    const int TimeoutSeconds = 5;
    static TimeSpan? offset;
    static UnityWebRequestAsyncOperation pending;
    static int host;
    static float nextAttempt;

    public static bool Known => offset.HasValue;
    // UTC from the internet when known, otherwise the PC clock.
    public static DateTime UtcNow => offset.HasValue ? DateTime.UtcNow + offset.Value : DateTime.UtcNow;
    public static void Reset() { pending?.webRequest?.Dispose(); pending = null; offset = null; host = 0; nextAttempt = 0f; }

    // Called every frame by MetaProgressionRuntime: one request at a time, all hosts in turn, then a pause before retrying.
    public static void Update()
    {
        if (offset.HasValue) return;
        if (pending != null) {
            if (!pending.isDone) return;
            var request = pending.webRequest; pending = null;
            // Any HTTP answer carries the server's Date header, also a redirect or an error status.
            if (request.responseCode > 0 && TryParse(request.GetResponseHeader("Date"), out var server)) offset = server - DateTime.UtcNow;
            request.Dispose();
            if (offset.HasValue) return;
            host = (host + 1) % Hosts.Length;
            if (host == 0) { nextAttempt = Time.realtimeSinceStartup + RetrySeconds; return; }
        }
        if (Time.realtimeSinceStartup < nextAttempt || Application.internetReachability == NetworkReachability.NotReachable) return;
        var head = UnityWebRequest.Head(Hosts[host]);
        head.timeout = TimeoutSeconds;
        pending = head.SendWebRequest();
    }
    // RFC 1123, e.g. "Fri, 23 Oct 2026 12:00:00 GMT".
    public static bool TryParse(string header, out DateTime utc)
    {
        bool ok = DateTime.TryParseExact(header, "r", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
        if (ok) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return ok;
    }
}
