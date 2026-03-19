using System;
using System.IO;
using CommonAPI;
using CommonAPI.Phone;
using Reptile;
using UnityEngine;

namespace BRCPanelPon
{
    public class AppPanelPon : CustomApp
    {
        private static Sprite IconSprite;

        private PanelPonGame _game;
        private PanelPonRenderer _renderer;

        private float _repeatDelayTimerX;
        private float _repeatDelayTimerY;
        private float _repeatRateTimerX;
        private float _repeatRateTimerY;

        private const float FirstRepeatDelay = 0.18f;
        private const float HeldRepeatRate = 0.09f;

        private bool _restartRightHeld;

        public override bool Available => true;

        public static void Initialize()
        {
            string iconPath = Path.Combine(PanelPonPlugin.Instance.Directory, "panelattack-appicon.png");

            if (File.Exists(iconPath))
                IconSprite = TextureUtility.LoadSprite(iconPath);

            if (IconSprite != null)
                PhoneAPI.RegisterApp<AppPanelPon>("panel atk", IconSprite);
            else
                PhoneAPI.RegisterApp<AppPanelPon>("panel atk");
        }

        public override void OnAppInit()
        {
            base.OnAppInit();

            if (IconSprite != null)
                CreateTitleBar("<size=75%>Panel de Pon</size>", IconSprite);
            else
                CreateIconlessTitleBar("size=75%>Panel de Pon</size>");

            _game = new PanelPonGame();
            _game.NewGame(Environment.TickCount);

            _renderer = new PanelPonRenderer(this);
            _renderer.Build();
            _renderer.Render(_game);
        }

        public override void OnAppEnable()
        {
            base.OnAppEnable();

            ResetInputRepeat();
            _restartRightHeld = false;
            PanelPonState.AppActive = true;

            FlushCurrentPlayerInput();

            if (_game == null)
            {
                _game = new PanelPonGame();
                _game.NewGame(Environment.TickCount);
            }

            if (_renderer == null)
            {
                _renderer = new PanelPonRenderer(this);
                _renderer.Build();
            }

            _renderer.Render(_game);
        }

        public override void OnAppDisable()
        {
            base.OnAppDisable();

            ResetInputRepeat();
            _restartRightHeld = false;
            PanelPonState.AppActive = false;

            FlushCurrentPlayerInput();
        }

        public override void OnAppUpdate()
        {
            base.OnAppUpdate();

            if (_game == null || _renderer == null)
                return;

            if (_game.IsGameOver)
            {
                if (PressedRestartRight())
                    StartNewGame();
            }
            else
            {
                HandleMovementInput(Time.unscaledDeltaTime);
                HandleActionInput();
            }

            _game.Tick(Time.unscaledDeltaTime);

            if (_game.GameOverThisTick)
                PlayGameOverSfx();

            _renderer.Render(_game);
        }

        private void FlushCurrentPlayerInput()
        {
            Player player = WorldHandler.instance?.GetCurrentPlayer();
            if (player != null)
                player.FlushInput();
        }

        private void HandleMovementInput(float dt)
        {
            float horizontal = ReadHorizontal();
            float vertical = ReadVertical();

            if (horizontal < -0.5f)
                HandleHeldAxis(ref _repeatDelayTimerX, ref _repeatRateTimerX, dt, -1, 0);
            else if (horizontal > 0.5f)
                HandleHeldAxis(ref _repeatDelayTimerX, ref _repeatRateTimerX, dt, 1, 0);
            else
                ResetHorizontalRepeat();

            if (vertical > 0.5f)
                HandleHeldAxis(ref _repeatDelayTimerY, ref _repeatRateTimerY, dt, 0, 1);
            else if (vertical < -0.5f)
                HandleHeldAxis(ref _repeatDelayTimerY, ref _repeatRateTimerY, dt, 0, -1);
            else
                ResetVerticalRepeat();
        }

        private void HandleHeldAxis(ref float delayTimer, ref float rateTimer, float dt, int dx, int dy)
        {
            bool firstPress = (delayTimer <= 0f && rateTimer <= 0f);

            if (firstPress)
            {
                if (CanMoveCursor(dx, dy))
                {
                    PlayMoveSfx();
                    _game.MoveCursor(dx, dy);
                }

                delayTimer = FirstRepeatDelay;
                rateTimer = 0f;
                return;
            }

            if (delayTimer > 0f)
            {
                delayTimer -= dt;
                return;
            }

            rateTimer -= dt;
            if (rateTimer <= 0f)
            {
                if (CanMoveCursor(dx, dy))
                {
                    PlayMoveSfx();
                    _game.MoveCursor(dx, dy);
                }

                rateTimer = HeldRepeatRate;
            }
        }

        private bool CanMoveCursor(int dx, int dy)
        {
            if (_game == null || _game.IsGameOver)
                return false;

            int newX = _game.CursorX + dx;
            int newY = _game.CursorY + dy;

            if (newX < 0 || newX > PanelPonGame.Width - 2)
                return false;

            if (newY < 0 || newY > PanelPonGame.Height - 1)
                return false;

            return true;
        }

        private void HandleActionInput()
        {
            if (PressedSwap())
            {
                if (_game.TrySwap())
                    PlaySwapSfx();
            }

            if (HeldRaise())
                _game.ManualRaise();
        }

        private bool HeldRaise()
        {
            return Input.GetKey(KeyCode.LeftShift)
                || Input.GetKey(KeyCode.K)
                || Input.GetKey(KeyCode.JoystickButton1);
        }

        private float ReadHorizontal()
        {
            float axis = 0f;

            if (Input.GetKey(KeyCode.A))
                axis = -1f;
            else if (Input.GetKey(KeyCode.D))
                axis = 1f;

            float joyAxis = Input.GetAxisRaw("Horizontal");
            if (Mathf.Abs(joyAxis) > Mathf.Abs(axis))
                axis = joyAxis;

            return axis;
        }

        private float ReadVertical()
        {
            float axis = 0f;

            if (Input.GetKey(KeyCode.S))
                axis = -1f;
            else if (Input.GetKey(KeyCode.W))
                axis = 1f;

            float joyAxis = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(joyAxis) > Mathf.Abs(axis))
                axis = joyAxis;

            return axis;
        }

        private bool PressedSwap()
        {
            return Input.GetKeyDown(KeyCode.J)
                || Input.GetKeyDown(KeyCode.Space)
                || Input.GetKeyDown(KeyCode.JoystickButton0);
        }

        private bool PressedRestartRight()
        {
            bool keyboardRight =
                Input.GetKeyDown(KeyCode.RightArrow) ||
                Input.GetKeyDown(KeyCode.D);

            float horizontal = Input.GetAxisRaw("Horizontal");
            bool controllerHeld = horizontal > 0.5f;

            bool controllerPressed = controllerHeld && !_restartRightHeld;
            _restartRightHeld = controllerHeld;

            return keyboardRight || controllerPressed;
        }

        private void StartNewGame()
        {
            _game.NewGame(Environment.TickCount);
            ResetInputRepeat();
            _restartRightHeld = false;
        }

        private void PlayMoveSfx()
        {
            if (PanelPonPlugin.Instance != null)
                PanelPonPlugin.Instance.PlayCursorSfx();
        }

        private void PlaySwapSfx()
        {
            if (PanelPonPlugin.Instance != null)
                PanelPonPlugin.Instance.PlaySwapSfx();
        }

        private void PlayGameOverSfx()
        {
            if (PanelPonPlugin.Instance != null)
                PanelPonPlugin.Instance.PlayDieSfx();
        }

        private void ResetInputRepeat()
        {
            ResetHorizontalRepeat();
            ResetVerticalRepeat();
        }

        private void ResetHorizontalRepeat()
        {
            _repeatDelayTimerX = 0f;
            _repeatRateTimerX = 0f;
        }

        private void ResetVerticalRepeat()
        {
            _repeatDelayTimerY = 0f;
            _repeatRateTimerY = 0f;
        }
    }
}