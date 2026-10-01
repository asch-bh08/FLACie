namespace FLACie.Core;

/// <summary>What Connect and Jams need from a player (the web player today, the Windows player later).</summary>
public interface IPlayerHost
{
    Track? Current { get; }
    IReadOnlyList<Track> Queue { get; }
    bool Playing { get; }
    long PositionMs { get; }
    /// <summary>0 off, 1 all, 2 one.</summary>
    int Repeat { get; }
    /// <summary>True once the current song has buffered enough to start at once.</summary>
    Task<bool> IsBufferedAsync();
    Task PlayFromAsync(IReadOnlyList<Track> tracks, int index, long positionMs, bool paused);
    Task PauseAsync();
    Task ResumeAsync();
    Task ToggleAsync();
    Task NextAsync();
    Task PrevAsync();
    /// <summary>Seeks without telling a Jam about it.</summary>
    Task SeekRawAsync(long ms);
    void AddNext(Track t);
    void AddLast(Track t);
    /// <summary>While in a Jam the player hands its buttons to the group instead of acting on them.</summary>
    IPlayInterceptor? Interceptor { get; set; }
    /// <summary>Set by a Jam: called when a song ends instead of moving on.</summary>
    Action? OnAutoAdvance { get; set; }
    event Action? Changed;
}

/// <summary>Each method returns true when it took the action (the player then does nothing itself).</summary>
public interface IPlayInterceptor
{
    bool Toggle();
    bool Seek(long ms);
    bool Next();
    bool Prev();
    bool SkipTo(int index);
    bool Play(IReadOnlyList<Track> list, int index);
    bool Enqueue(Track t, bool next);
}
