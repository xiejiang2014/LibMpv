namespace LibMpv.MVVM;

public enum FileState
{
    Closed,
    Loading,
    Buffering,
    Loaded,
}

public enum PlayerState
{
    Playing,

    Paused,
    /// <summary>
    /// 播放完毕
    /// </summary>
    End
}