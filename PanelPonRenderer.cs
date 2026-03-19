using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BRCPanelPon
{
    public class PanelPonRenderer
    {
        private readonly AppPanelPon _app;

        private GameObject _root;
        private RectTransform _rootRect;

        private Image _boardBackground;

        // NEW: masked playfield so the incoming row is clipped correctly
        private GameObject _playfieldMaskObject;
        private RectTransform _playfieldMaskRect;

        private Image[,] _cells;
        private Image[] _incomingCells;
        private Image _cursorImage;
        private Image _overlayBackground;
        private TextMeshProUGUI _gameOverText;
        private TextMeshProUGUI _scoreText;
        private TextMeshProUGUI _personalBestText;
        private TextMeshProUGUI _restartText;
        private TextMeshProUGUI _quitText;

        private const int Width = PanelPonGame.Width;
        private const int Height = PanelPonGame.Height;

        private const float RootScale = 7f;

        private const int BlockFrameWidth = 16;
        private const int BlockFrameHeight = 16;

        private const int CursorFrameWidth = 38;
        private const int CursorFrameHeight = 22;
        private const int CursorFrameCount = 2;
        private const float CursorAnimFps = 6f;

        // Frame indices based on the clone logic:
        // idle default = 0
        // incoming next-line = 1
        // clear blink cycles through clear frames
        // clear face uses face frame
        // land uses land animation sequence
        // danger uses danger animation sequence
        private static readonly int[] LandFrames = { 2, 1, 0, 1 };
        private static readonly int[] DangerFrames = { 2, 3, 4 };
        private const int IdleFrame = 0;
        private const int IncomingFrame = 1;
        private const int ClearFaceFrame = 5;
        private const int ClearHighlightFrame = 6;

        private float _cellSize;
        private float _cellGap;
        private float _boardWidth;
        private float _boardHeight;

        private readonly Dictionary<BlockType, Texture2D> _blockTextures = new Dictionary<BlockType, Texture2D>();
        private readonly List<Sprite> _cursorFrames = new List<Sprite>();
        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

        public PanelPonRenderer(AppPanelPon app)
        {
            _app = app;
        }

        public void Build()
        {
            if (_root != null)
                Object.Destroy(_root);

            _spriteCache.Clear();
            LoadSprites();
            CalculateLayout();

            _root = new GameObject("PanelDePonRoot", typeof(RectTransform));
            _rootRect = _root.GetComponent<RectTransform>();
            _rootRect.SetParent(_app.transform, false);
            _rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            _rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            _rootRect.pivot = new Vector2(0.5f, 0.5f);
            _rootRect.sizeDelta = new Vector2(_boardWidth + 12f, _boardHeight + 12f);
            _rootRect.anchoredPosition = new Vector2(0f, -80f);
            _rootRect.localScale = Vector3.one * RootScale;

            var bgGO = new GameObject("BoardBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.SetParent(_rootRect, false);
            bgRect.anchorMin = new Vector2(0.5f, 0.5f);
            bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.sizeDelta = new Vector2(_boardWidth + 4f, _boardHeight + 4f);
            bgRect.anchoredPosition = Vector2.zero;

            _boardBackground = bgGO.GetComponent<Image>();
            _boardBackground.color = new Color(0.05f, 0.05f, 0.08f, 0.96f);

            CreatePlayfieldMask();

            _cells = new Image[Width, Height];
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    _cells[x, y] = CreateBlockImage(
                        $"Cell_{x}_{y}",
                        GetCellPosition(x, y),
                        _playfieldMaskRect
                    );
                }
            }

            _incomingCells = new Image[Width];
            for (int x = 0; x < Width; x++)
            {
                _incomingCells[x] = CreateBlockImage(
                    $"Incoming_{x}",
                    Vector2.zero,
                    _playfieldMaskRect
                );
            }

            _cursorImage = CreateCursorImage();
            CreateOverlayBackground();
            CreateGameOverText();
            CreateScoreText();
            CreatePersonalBestText();
            CreateRestartText();
            CreateQuitText();
        }

        private void CreatePlayfieldMask()
        {
            _playfieldMaskObject = new GameObject(
                "PlayfieldMask",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RectMask2D)
            );

            _playfieldMaskRect = _playfieldMaskObject.GetComponent<RectTransform>();
            _playfieldMaskRect.SetParent(_rootRect, false);
            _playfieldMaskRect.anchorMin = new Vector2(0.5f, 0.5f);
            _playfieldMaskRect.anchorMax = new Vector2(0.5f, 0.5f);
            _playfieldMaskRect.pivot = new Vector2(0.5f, 0.5f);
            _playfieldMaskRect.sizeDelta = new Vector2(_boardWidth, _boardHeight);
            _playfieldMaskRect.anchoredPosition = Vector2.zero;

            // Invisible image required for RectMask2D host.
            var img = _playfieldMaskObject.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.001f);
        }

        public void Render(PanelPonGame game)
        {
            if (_cells == null)
                return;

            float pushOffset = game.GetPushVisualOffsetPixels();

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    Image cell = _cells[x, y];
                    PanelBlock block = game.Grid[x, y];

                    cell.rectTransform.anchoredPosition = GetCellPosition(x, y) + new Vector2(0f, pushOffset);

                    if (block.Type == BlockType.Empty || block.AnimState == PanelBlockAnim.ClearDead)
                    {
                        cell.enabled = false;
                        cell.sprite = null;
                        continue;
                    }

                    cell.enabled = true;
                    cell.sprite = GetBlockSprite(block);
                    cell.color = GetBlockColor(block, false, game.TotalTicks, x, y, game);
                }
            }

            for (int x = 0; x < Width; x++)
            {
                PanelBlock block = game.NextLine[x];
                Image cell = _incomingCells[x];

                if (block.Type == BlockType.Empty)
                {
                    cell.enabled = false;
                    cell.sprite = null;
                    continue;
                }

                cell.enabled = true;
                cell.sprite = GetBlockSprite(block, IncomingFrame);
                cell.color = new Color(0.42f, 0.42f, 0.42f, 0.95f);
                cell.rectTransform.anchoredPosition = GetIncomingCellPosition(x, pushOffset);
            }

            UpdateCursor(game.CursorX, game.CursorY, pushOffset);

            bool showGameOverUi = game.IsGameOver;

            if (_cursorImage != null)
                _cursorImage.enabled = !showGameOverUi && _cursorFrames.Count > 0;

            if (_overlayBackground != null)
                _overlayBackground.enabled = showGameOverUi;

            if (_gameOverText != null)
                _gameOverText.enabled = showGameOverUi;

            if (_scoreText != null)
            {
                _scoreText.enabled = showGameOverUi;
                _scoreText.text = $"Total Score: {game.Score}";
            }

            if (_personalBestText != null)
            {
                _personalBestText.enabled = showGameOverUi;
                _personalBestText.text = $"High-Score: {game.PersonalBest}";
            }

            if (_restartText != null)
                _restartText.enabled = showGameOverUi;

            if (_quitText != null)
                _quitText.enabled = showGameOverUi;
        }

        private void LoadSprites()
        {
            _blockTextures.Clear();
            _cursorFrames.Clear();

            string spriteFolder = Path.Combine(PanelPonPlugin.Instance.Directory, "Sprites");

            _blockTextures[BlockType.Red] = LoadTexture(Path.Combine(spriteFolder, "block_red.png"));
            _blockTextures[BlockType.Blue] = LoadTexture(Path.Combine(spriteFolder, "block_blue.png"));
            _blockTextures[BlockType.Green] = LoadTexture(Path.Combine(spriteFolder, "block_green.png"));
            _blockTextures[BlockType.Yellow] = LoadTexture(Path.Combine(spriteFolder, "block_yellow.png"));
            _blockTextures[BlockType.Purple] = LoadTexture(Path.Combine(spriteFolder, "block_purple.png"));
            _blockTextures[BlockType.DarkBlue] = LoadTexture(Path.Combine(spriteFolder, "block_darkblue.png"));

            string cursorPath = Path.Combine(spriteFolder, "cursor.png");
            for (int i = 0; i < CursorFrameCount; i++)
            {
                Sprite frame = LoadCursorStripFrame(cursorPath, i);
                if (frame != null)
                    _cursorFrames.Add(frame);
            }
        }

        private Texture2D LoadTexture(string path)
        {
            if (!File.Exists(path))
                return null;

            byte[] bytes = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.LoadImage(bytes);
            return texture;
        }

        private Sprite LoadCursorStripFrame(string path, int frameIndex)
        {
            if (!File.Exists(path))
                return null;

            Texture2D texture = LoadTexture(path);
            int framesWide = Mathf.Max(1, texture.width / CursorFrameWidth);
            int clampedFrame = Mathf.Clamp(frameIndex, 0, framesWide - 1);

            int frameX = clampedFrame * CursorFrameWidth;
            Rect rect = new Rect(frameX, 0, CursorFrameWidth, CursorFrameHeight);

            return Sprite.Create(
                texture,
                rect,
                new Vector2(0.5f, 0.5f),
                BlockFrameWidth,
                0,
                SpriteMeshType.FullRect
            );
        }

        private Sprite GetBlockSprite(PanelBlock block)
        {
            return GetBlockSprite(block, GetFrameIndex(block));
        }

        private Sprite GetBlockSprite(PanelBlock block, int forceFrame)
        {
            if (!_blockTextures.TryGetValue(block.Type, out Texture2D texture) || texture == null)
                return null;

            int framesWide = Mathf.Max(1, texture.width / BlockFrameWidth);
            int frame = Mathf.Clamp(forceFrame, 0, framesWide - 1);

            string key = $"{block.Type}_{frame}";
            if (_spriteCache.TryGetValue(key, out Sprite cached))
                return cached;

            Rect rect = new Rect(frame * BlockFrameWidth, 0, BlockFrameWidth, BlockFrameHeight);

            Sprite sprite = Sprite.Create(
                texture,
                rect,
                new Vector2(0.5f, 0.5f),
                BlockFrameWidth,
                0,
                SpriteMeshType.FullRect
            );

            _spriteCache[key] = sprite;
            return sprite;
        }

        private int GetFrameIndex(PanelBlock block)
        {
            if (block.State == PanelBlockState.Clear)
            {
                if (block.ClearComboSize > 3 && block.AnimState == PanelBlockAnim.ClearHighlight)
                    return ClearHighlightFrame;

                return ClearFaceFrame;
            }

            switch (block.AnimState)
            {
                case PanelBlockAnim.Land:
                    {
                        int idx = Mathf.Clamp(LandFrames.Length - 1 - block.AnimCounter, 0, LandFrames.Length - 1);
                        return LandFrames[idx];
                    }

                case PanelBlockAnim.Danger:
                    return DangerFrames[(int)(Time.unscaledTime * 8f) % DangerFrames.Length];

                default:
                    return IdleFrame;
            }
        }

        private Color GetBlockColor(PanelBlock block, bool incoming, long totalTicks, int x, int y, PanelPonGame game)
        {
            if (incoming)
                return new Color(0.42f, 0.42f, 0.42f, 0.95f);

            return Color.white;
        }

        private bool IsDangerBlock(int x, int y, PanelPonGame game)
        {
            int warningHeight = 2;
            for (int yy = Height - 1; yy > (Height - 1) - warningHeight; yy--)
            {
                if (game.Grid[x, yy].Type != BlockType.Empty)
                    return true;
            }
            return false;
        }

        private void CalculateLayout()
        {
            _cellSize = 16f;
            _cellGap = 0f;

            _boardWidth = Width * _cellSize + (Width - 1) * _cellGap;
            _boardHeight = Height * _cellSize + (Height - 1) * _cellGap;
        }

        private Image CreateBlockImage(string name, Vector2 pos, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_cellSize, _cellSize);
            rect.anchoredPosition = pos;

            var image = go.GetComponent<Image>();
            image.preserveAspect = true;
            image.color = Color.white;
            image.enabled = false;
            return image;
        }

        private Image CreateCursorImage()
        {
            var go = new GameObject("Cursor", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_rootRect, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(CursorFrameWidth, CursorFrameHeight);

            var image = go.GetComponent<Image>();
            image.sprite = _cursorFrames.Count > 0 ? _cursorFrames[0] : null;
            image.preserveAspect = true;
            image.color = Color.white;
            image.enabled = _cursorFrames.Count > 0;

            return image;
        }

        private void CreateOverlayBackground()
        {
            var go = new GameObject("OverlayBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_rootRect, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_boardWidth + 4f, _boardHeight + 4f);
            rect.anchoredPosition = Vector2.zero;

            _overlayBackground = go.GetComponent<Image>();
            _overlayBackground.color = new Color(0f, 0f, 0f, 0.72f);
            _overlayBackground.enabled = false;
        }

        private void CreateGameOverText()
        {
            var go = new GameObject("GameOverText", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_rootRect, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_boardWidth + 30f, 24f);
            rect.anchoredPosition = new Vector2(0f, 24f);

            _gameOverText = go.AddComponent<TextMeshProUGUI>();
            _gameOverText.text = "GAME OVER";
            _gameOverText.alignment = TextAlignmentOptions.Center;
            _gameOverText.fontSize = 10f;
            _gameOverText.color = Color.white;
            _gameOverText.enabled = false;
        }

        private void CreateScoreText()
        {
            var go = new GameObject("ScoreText", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_rootRect, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_boardWidth + 60f, 18f);
            rect.anchoredPosition = new Vector2(0f, 6f);

            _scoreText = go.AddComponent<TextMeshProUGUI>();
            _scoreText.text = "Total Score: 0";
            _scoreText.alignment = TextAlignmentOptions.Center;
            _scoreText.fontSize = 7.5f;
            _scoreText.color = Color.white;
            _scoreText.enabled = false;
        }

        private void CreatePersonalBestText()
        {
            var go = new GameObject("PersonalBestText", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_rootRect, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_boardWidth + 90f, 18f);
            rect.anchoredPosition = new Vector2(0f, -10f);

            _personalBestText = go.AddComponent<TextMeshProUGUI>();
            _personalBestText.text = "High-Score: 0";
            _personalBestText.alignment = TextAlignmentOptions.Center;
            _personalBestText.fontSize = 7f;
            _personalBestText.color = Color.white;
            _personalBestText.enabled = false;
        }

        private void CreateRestartText()
        {
            var go = new GameObject("RestartText", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_rootRect, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_boardWidth + 90f, 18f);
            rect.anchoredPosition = new Vector2(0f, -28f);

            _restartText = go.AddComponent<TextMeshProUGUI>();
            _restartText.text = "PRESS RIGHT TO RESTART";
            _restartText.alignment = TextAlignmentOptions.Center;
            _restartText.fontSize = 6f;
            _restartText.color = Color.white;
            _restartText.enabled = false;
        }

        private void CreateQuitText()
        {
            var go = new GameObject("QuitText", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_rootRect, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(_boardWidth + 90f, 18f);
            rect.anchoredPosition = new Vector2(0f, -40f);

            _quitText = go.AddComponent<TextMeshProUGUI>();
            _quitText.text = "PRESS LEFT TO QUIT";
            _quitText.alignment = TextAlignmentOptions.Center;
            _quitText.fontSize = 6f;
            _quitText.color = Color.white;
            _quitText.enabled = false;
        }

        private void UpdateCursor(int x, int y, float pushOffset)
        {
            if (_cursorImage == null)
                return;

            _cursorImage.enabled = _cursorFrames.Count > 0;
            if (_cursorFrames.Count == 0)
                return;

            int frameIndex = (int)(Time.unscaledTime * CursorAnimFps) % _cursorFrames.Count;
            _cursorImage.sprite = _cursorFrames[frameIndex];

            Vector2 left = GetCellPosition(x, y) + new Vector2(0f, pushOffset);
            Vector2 right = GetCellPosition(x + 1, y) + new Vector2(0f, pushOffset);
            Vector2 center = (left + right) * 0.5f;

            _cursorImage.rectTransform.anchoredPosition = center;
        }

        private Vector2 GetCellPosition(int x, int y)
        {
            float startX = -_boardWidth * 0.5f + _cellSize * 0.5f;
            float startY = -_boardHeight * 0.5f + _cellSize * 0.5f;

            float px = startX + x * (_cellSize + _cellGap);
            float py = startY + y * (_cellSize + _cellGap);

            return new Vector2(px, py);
        }

        private Vector2 GetIncomingCellPosition(int x, float pushOffset)
        {
            float startX = -_boardWidth * 0.5f + _cellSize * 0.5f;
            float px = startX + x * (_cellSize + _cellGap);

            // place just below the visible field so RectMask2D clips it
            float py = -_boardHeight * 0.5f - (_cellSize * 0.5f) + pushOffset;

            return new Vector2(px, py);
        }
    }
}