using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibMpv.Client;
using System;
using System.Globalization;
using System.Threading;

namespace LibMpv.MVVM;

[ObservableObject]
public abstract partial class BaseMpvContextViewModel : MpvContext
{
    public FileState FileState { get; private set; } = FileState.Closed;


    PlayerState        _playerState = PlayerState.Playing;
    public PlayerState PlayerState => _playerState;

    public event Action<PlayerState, PlayerState> PlayerStateChanging;
    public event Action<PlayerState>              PlayerStateChanged;

    public event Action<FileState, FileState> FileStateChanging;
    public event Action<FileState>            FileStateChanged;


    public bool IsPaused
    {
        get => this.GetPropertyFlag("pause") ?? true;
    }

    public long Volume
    {
        get => this.GetPropertyLong("volume") ?? 0;
        set => this.SetPropertyLong("volume", value);
    }

    public bool IsMuted
    {
        get => this.GetPropertyFlag("mute") ?? false;
    }

    public bool IsSeekable
    {
        get => this.GetPropertyFlag("seekable") ?? false;
    }

    public bool IsBuffering
    {
        get => this.GetPropertyFlag("paused-for-cache") ?? false;
    }

    public long CacheBufferingState
    {
        get => this.GetPropertyLong("cache-buffering-state") ?? 0;
    }

    public TimeSpan Duration
    {
        get
        {
            var duration = this.GetPropertyDouble("duration");
            if (duration == null)
                return TimeSpan.FromSeconds(0);
            return TimeSpan.FromSeconds(duration.Value);
        }
    }

    public TimeSpan TimeRemaining
    {
        get
        {
            var remain = this.GetPropertyDouble("time-remaining");
            if (remain == null)
                return TimeSpan.FromSeconds(0);
            return TimeSpan.FromSeconds(remain.Value);
        }
    }

    public TimeSpan TimePos
    {
        get
        {
            var position = this.GetPropertyDouble("time-pos");
            if (position == null)
                return TimeSpan.FromSeconds(0);
            return TimeSpan.FromSeconds(position.Value);
        }
        set
        {
            if (value == null) return;
            var position       = value.TotalSeconds;
            var positionString = position.ToString(CultureInfo.InvariantCulture);
            this.Command("seek", positionString, "absolute");
        }
    }

    public long PercentPos
    {
        get => this.GetPropertyLong("percent-pos") ?? 0;
        set
        {
            if (value >= 0 && value <= 100)
                this.SetPropertyLong("percent-pos", value);
        }
    }

    public string Path
    {
        get => this.GetPropertyString("path");
        set
        {
            if (value != null)
            {
                this.Stop();
                this.Command("loadfile", value, "replace");
            }
        }
    }

    public BaseMpvContextViewModel()
    {
        InitPropertyObserver();
        this.EndFile    += Context_EndFile;
        this.FileLoaded += Context_FileLoaded;
        this.StartFile  += Context_StartFile;
    }

    public abstract void InvokeInUIThread(Action action);

    private void RaisePropertyChangedInUIThread(string propertyName)
    {
        InvokeInUIThread(() => OnPropertyChanged(propertyName));
    }


    private void SetFileState(FileState newState)
    {
        System.Diagnostics.Debug.WriteLine($"Changing FileState to : {newState}");

        FileStateChanging?.Invoke(FileState, newState);

        FileState = newState;

        OnPropertyChanged(nameof(FileState));

        FileStateChanged?.Invoke(FileState);

        InvokeInUIThread(() =>
                         {
                             StopCommand.NotifyCanExecuteChanged();
                             PlayCommand.NotifyCanExecuteChanged();
                             PauseCommand.NotifyCanExecuteChanged();
                             TogglePlayPauseCommand.NotifyCanExecuteChanged();
                         });
    }


    private void SetPlayerState(PlayerState newState)
    {
        System.Diagnostics.Debug.WriteLine($"Changing player state to : {newState}");

        PlayerStateChanging?.Invoke(_playerState, newState);

        _playerState = newState;

        OnPropertyChanged(nameof(PlayerState));

        PlayerStateChanged?.Invoke(_playerState);

        InvokeInUIThread(() =>
                         {
                             StopCommand.NotifyCanExecuteChanged();
                             PlayCommand.NotifyCanExecuteChanged();
                             PauseCommand.NotifyCanExecuteChanged();
                             TogglePlayPauseCommand.NotifyCanExecuteChanged();
                         });
    }

    [RelayCommand]
    protected void ToggleMute()
    {
        this.Command("cycle", "mute");
    }

    [RelayCommand(CanExecute = nameof(CanMute))]
    public void Mute()
    {
        this.SetPropertyFlag("mute", true);
    }

    bool CanMute()
    {
        return !IsMuted;
    }

    [RelayCommand(CanExecute = nameof(CanUnMute))]
    public void UnMute()
    {
        this.SetPropertyFlag("mute", false);
    }

    bool CanUnMute()
    {
        return IsMuted;
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    public void Stop()
    {
        this.Command("stop");
    }

    bool CanStop()
    {
        return FileState != FileState.Closed;
    }

    [RelayCommand(CanExecute = nameof(CanTogglePlayPause))]
    public void TogglePlayPause()
    {
        this.Command("cycle", "pause");
    }

    bool CanTogglePlayPause()
    {
        return this.PlayerState == PlayerState.Playing || this.PlayerState == PlayerState.Paused;
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    public void Play()
    {
        this.SetPropertyFlag("pause", false);
    }

    bool CanPlay()
    {
        return FileState == FileState.Loaded && PlayerState == PlayerState.Paused;
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    public void Pause()
    {
        this.SetPropertyFlag("pause", true);
    }

    bool CanPause()
    {
        return FileState == FileState.Loaded && PlayerState == PlayerState.Playing;
    }

    private void Context_StartFile(object sender, EventArgs e)
    {
        SetFileState(FileState.Loading);
    }

    private void Context_FileLoaded(object sender, EventArgs e)
    {
        SetFileState(FileState.Loaded);

        if (IsPaused)
        {
            SetPlayerState(PlayerState.Paused);
        }
        else
        {
            SetPlayerState(PlayerState.Playing);
        }
    }

    private void Context_EndFile(object sender, MpvEndFileEventArgs e)
    {
        SetFileState(FileState.Closed);
        SetPlayerState(PlayerState.End);
    }

    //实测 调用本函数后,PlayerState 会是 Loading 并立即返回, 如果在 Loading 状态下设置某些属性比如 TimePos, 会导致异常
    //另外,如果播放状态是 Playing 那么 LoadFile 完成后会自动播放, 如果播放状态是 Pause 那么 LoadFile 完成后会暂停
    public void LoadFile(string fileName, string mode = "replace")
    {
        Command("loadfile", fileName, mode);
    }

    public void Seek(long amount, string reference = "relative", string precision = "keyframes")
    {
        Command("seek", amount.ToString(), reference, precision);
    }

    public void FrameStep()
    {
        Command("frame-step");
    }

    public void FrameBackStep()
    {
        Command("frame-back-step");
    }

    protected virtual void InitPropertyObserver()
    {
        this.ObserveProperty("paused-for-cache", (bool isBuffering) =>
                                                 {
                                                     RaisePropertyChangedInUIThread(nameof(IsBuffering));
                                                     if (isBuffering)
                                                     {
                                                         SetFileState(FileState.Buffering);
                                                     }
                                                     else if (!String.IsNullOrEmpty(Path))
                                                     {
                                                         if (FileState != FileState.Loaded)
                                                         {
                                                             SetFileState(FileState.Loaded);
                                                         }
                                                     }
                                                     else
                                                     {
                                                         SetFileState(FileState.Closed);
                                                     }
                                                 });

        this.ObserveProperty("pause", (bool isPaused) =>
                                      {
                                          RaisePropertyChangedInUIThread(nameof(IsPaused));

                                          if (isPaused)
                                          {
                                              SetPlayerState(PlayerState.Paused);
                                          }
                                          else if (!String.IsNullOrEmpty(Path))
                                          {
                                              SetPlayerState(PlayerState.Playing);
                                          }
                                          else
                                          {
                                              SetFileState(FileState.Closed);
                                          }
                                      });


        this.ObserveProperty("cache-buffering-state", () => RaisePropertyChangedInUIThread(nameof(CacheBufferingState)));
        this.ObserveProperty("duration",              () => RaisePropertyChangedInUIThread(nameof(Duration)));
        this.ObserveProperty("time-pos",              () => RaisePropertyChangedInUIThread(nameof(TimePos)));
        this.ObserveProperty("time-remaining",        () => RaisePropertyChangedInUIThread(nameof(TimeRemaining)));
        this.ObserveProperty("mute",                  () => RaisePropertyChangedInUIThread(nameof(IsMuted)));
        this.ObserveProperty("volume",                () => RaisePropertyChangedInUIThread(nameof(Volume)));
        this.ObserveProperty("seekable",              () => RaisePropertyChangedInUIThread(nameof(IsSeekable)));
        this.ObserveProperty("path",                  () => RaisePropertyChangedInUIThread(nameof(Path)));
        this.ObserveProperty("percent-pos",           () => RaisePropertyChangedInUIThread(nameof(PercentPos)));
    }
}