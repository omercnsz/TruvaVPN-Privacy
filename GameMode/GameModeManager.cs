using System;

namespace TruvaDesktop.GameMode
{
    public class GameModeManager
    {
        private static GameModeManager? _instance;
        public static GameModeManager Instance => _instance ??= new GameModeManager();

        private readonly FragmenterService _fragmenterService;
        public Action<bool>? StateChanged { get; set; }

        public bool IsRunning => _fragmenterService.IsRunning;

        private GameModeManager()
        {
            _fragmenterService = new FragmenterService();
        }

        public void ToggleMode()
        {
            if (IsRunning)
            {
                Stop();
            }
            else
            {
                Start();
            }
        }

        public void Start()
        {
            if (!IsRunning)
            {
                _fragmenterService.Start();
                StateChanged?.Invoke(true);
            }
        }

        public void Stop()
        {
            if (IsRunning)
            {
                _fragmenterService.Stop();
                StateChanged?.Invoke(false);
            }
        }
    }
}
