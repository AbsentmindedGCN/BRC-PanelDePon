using System;
using System.Collections.Generic;
using UnityEngine;

namespace BRCPanelPon
{
    public class ClearResult
    {
        public int Combo;
        public bool Chain;

        public ClearResult(int combo, bool chain)
        {
            Combo = combo;
            Chain = chain;
        }
    }

    public enum BlockType
    {
        Empty = 0,
        Red,
        Blue,
        Green,
        Yellow,
        Purple,
        DarkBlue
    }

    public enum PanelBlockState
    {
        Static = 0,
        Hang = 1,
        Fall = 2,
        Swap = 3,
        Clear = 4
    }

    public enum PanelBlockAnim
    {
        None = -1,
        SwapLeft = 0,
        SwapRight = 1,
        Land = 2,
        ClearFace = 3,
        ClearHighlight = 4,
        ClearDead = 5,
        Danger = 6
    }

    [Serializable]
    public class PanelBlock
    {
        public BlockType Type = BlockType.Empty;
        public PanelBlockState State = PanelBlockState.Static;
        public int Counter = 0;

        public PanelBlockAnim AnimState = PanelBlockAnim.None;
        public int AnimCounter = 0;
        public int ExplodeCounter = 0;

        public bool Chain = false;
        public int ClearComboSize = 0;

        public bool IsEmpty()
        {
            return Counter == 0 && Type == BlockType.Empty;
        }

        public bool IsSupport()
        {
            return State != PanelBlockState.Fall && Type != BlockType.Empty;
        }
    }

    public class PanelPonGame
    {
        public const int Width = 6;
        public const int Height = 12;

        public const int HANGTIME = 11;
        public const int FALLTIME = 4;
        public const int SWAPTIME = 4;
        public const int CLEARBLINKTIME = 38;
        public const int CLEARPAUSETIME = 20;
        public const int CLEAREXPLODETIME = 8;
        public const int PUSHTIME = 1000;

        public PanelBlock[,] Grid = new PanelBlock[Width, Height];
        public PanelBlock[] NextLine = new PanelBlock[Width];

        public int CursorX { get; private set; } = 2;
        public int CursorY { get; private set; } = 5;

        public bool IsGameOver { get; private set; }
        public bool ClearedThisTick { get; private set; }
        public bool GameOverThisTick { get; private set; }

        public int Score { get; private set; }
        public int PersonalBest { get; private set; }
        public int CurrentChain { get; private set; }

        public int PushTime => PUSHTIME;
        public int PushCounter { get; private set; }
        public long TotalTicks { get; private set; }

        private System.Random _rng;
        private float _tickAccumulator;

        private bool _playedLandSfxThisFrame;

        private int _activeClearSoundChain;
        private int _activeClearSoundStep;
        private int _activeClearSoundMaxSteps;

        public PanelPonGame()
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                    Grid[x, y] = new PanelBlock();

                NextLine[x] = new PanelBlock();
            }
        }

        public void NewGame(int seed)
        {
            _rng = new System.Random(seed);
            _tickAccumulator = 0f;
            TotalTicks = 0;

            ClearBoard();

            FillStartingRows(4);
            FillNextLine();

            CursorX = 2;
            CursorY = 5;

            PushCounter = PUSHTIME;
            IsGameOver = false;
            ClearedThisTick = false;
            GameOverThisTick = false;
            Score = 0;
            CurrentChain = 0;

            _activeClearSoundChain = 1;
            _activeClearSoundStep = 0;
            _activeClearSoundMaxSteps = 0;

            for (int i = 0; i < 20; i++)
            {
                UpdateStateStep();
                ClearResult result = UpdateCombosAndChains();
                if (result.Combo == 0)
                    break;
            }
        }

        public void Tick(float dt)
        {
            ClearedThisTick = false;
            GameOverThisTick = false;

            if (IsGameOver)
                return;

            _tickAccumulator += dt * 60f;

            while (_tickAccumulator >= 1f)
            {
                _tickAccumulator -= 1f;
                TickOneFrame();

                if (IsGameOver)
                    break;
            }
        }

        private void TickOneFrame()
        {
            TotalTicks++;
            _playedLandSfxThisFrame = false;

            UpdateStateStep();

            ClearResult result = UpdateCombosAndChains();
            if (result.Combo > 0)
            {
                ClearedThisTick = true;
                AddScoreForClear(result.Combo, result.Chain);

                if (PanelPonPlugin.Instance != null)
                    PanelPonPlugin.Instance.PlayClearSfx();

                _activeClearSoundChain = Mathf.Clamp(CurrentChain, 1, 4);
                _activeClearSoundStep = 0;
                _activeClearSoundMaxSteps = result.Combo;
            }
            else if (!HasPendingChainBlocks())
            {
                CurrentChain = 0;
            }

            PushCounter--;
            if (PushCounter <= 0)
            {
                PushCounter = PUSHTIME;
                Push();
            }

            CheckGameOverImmediate();
        }

        public bool MoveCursor(int dx, int dy)
        {
            if (IsGameOver)
                return false;

            int oldX = CursorX;
            int oldY = CursorY;

            CursorX = Mathf.Clamp(CursorX + dx, 0, Width - 2);
            CursorY = Mathf.Clamp(CursorY + dy, 0, Height - 1);

            return CursorX != oldX || CursorY != oldY;
        }

        public bool TrySwap()
        {
            if (IsGameOver)
                return false;

            if (!IsSwappable(CursorX, CursorY) || !IsSwappable(CursorX + 1, CursorY))
                return false;

            SwapBlocks(CursorX, CursorY);
            return true;
        }

        public void ManualRaise()
        {
            if (IsGameOver)
                return;

            PushCounter -= 20;
            if (PushCounter < 1)
                PushCounter = 1;
        }

        public float GetPushVisualOffsetPixels()
        {
            float pushed = (PUSHTIME - Mathf.Clamp(PushCounter, 0, PUSHTIME)) / (float)PUSHTIME;
            return pushed * 16f;
        }

        public bool IsIncomingRowDarkened()
        {
            return true;
        }

        private void SwapBlocks(int x, int y)
        {
            PanelBlock a = Grid[x, y];
            PanelBlock b = Grid[x + 1, y];

            BlockType tempType = b.Type;
            bool tempChain = b.Chain;

            b.Type = a.Type;
            b.Chain = false;

            a.Type = tempType;
            a.Chain = false;

            a.State = PanelBlockState.Swap;
            b.State = PanelBlockState.Swap;

            if (a.Type == BlockType.Empty)
            {
                a.Counter = 0;
                a.AnimState = PanelBlockAnim.None;
                a.AnimCounter = 0;
            }
            else
            {
                a.Counter = SWAPTIME;
                a.AnimState = PanelBlockAnim.SwapLeft;
                a.AnimCounter = SWAPTIME;
                a.Chain = tempChain;
            }

            if (b.Type == BlockType.Empty)
            {
                b.Counter = 0;
                b.AnimState = PanelBlockAnim.None;
                b.AnimCounter = 0;
            }
            else
            {
                b.Counter = SWAPTIME;
                b.AnimState = PanelBlockAnim.SwapRight;
                b.AnimCounter = SWAPTIME;
                b.Chain = false;
            }
        }

        private void UpdateStateStep()
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    UpdateBlockState(x, y);
                }
            }
        }

        private void UpdateBlockState(int x, int y)
        {
            PanelBlock block = Grid[x, y];

            if (block.AnimCounter > 0)
                block.AnimCounter--;

            if (block.State == PanelBlockState.Clear)
            {
                if (block.ClearComboSize > 3 &&
                    block.AnimState == PanelBlockAnim.ClearHighlight &&
                    block.Counter <= block.ExplodeCounter + 18)
                {
                    block.AnimState = PanelBlockAnim.ClearFace;
                    block.AnimCounter = 18;
                }
            }
            else if (block.AnimCounter <= 0)
            {
                if (block.AnimState == PanelBlockAnim.Land)
                    block.AnimState = PanelBlockAnim.None;
            }

            if (block.Counter > 0)
            {
                block.Counter--;
                if (block.Counter > 0)
                {
                    ApplyDangerAnimationIfNeeded(x, y, block);
                    return;
                }
            }

            switch (block.State)
            {
                case PanelBlockState.Static:
                case PanelBlockState.Swap:
                    if (block.Type == BlockType.Empty)
                    {
                        block.State = PanelBlockState.Static;
                        block.Chain = false;
                        break;
                    }

                    if (y == 0)
                    {
                        block.State = PanelBlockState.Static;
                        block.Chain = false;
                    }
                    else if (Grid[x, y - 1].State == PanelBlockState.Hang)
                    {
                        block.State = PanelBlockState.Hang;
                        block.Counter = Grid[x, y - 1].Counter;
                        block.Chain = Grid[x, y - 1].Chain;
                    }
                    else if (Grid[x, y - 1].IsEmpty())
                    {
                        block.State = PanelBlockState.Hang;
                        block.Counter = HANGTIME;
                    }
                    else
                    {
                        block.Chain = false;
                    }
                    break;

                case PanelBlockState.Hang:
                    block.State = PanelBlockState.Fall;
                    goto case PanelBlockState.Fall;

                case PanelBlockState.Fall:
                    if (y > 0 && Grid[x, y - 1].IsEmpty())
                    {
                        FallOneCell(x, y);
                        return;
                    }
                    else if (y > 0 && Grid[x, y - 1].State == PanelBlockState.Clear)
                    {
                        block.State = PanelBlockState.Static;
                    }
                    else if (y > 0)
                    {
                        block.State = Grid[x, y - 1].State;
                        block.Counter = Grid[x, y - 1].Counter;

                        if (Grid[x, y - 1].Chain)
                            block.Chain = true;
                    }
                    else
                    {
                        block.State = PanelBlockState.Static;
                    }

                    if ((block.State == PanelBlockState.Static || block.State == PanelBlockState.Swap)
                        && block.Type != BlockType.Empty)
                    {
                        block.AnimState = PanelBlockAnim.Land;
                        block.AnimCounter = 4;

                        if (!_playedLandSfxThisFrame)
                        {
                            _playedLandSfxThisFrame = true;

                            if (PanelPonPlugin.Instance != null)
                                PanelPonPlugin.Instance.PlayThumpSfx();
                        }
                    }
                    break;

                case PanelBlockState.Clear:
                    EraseBlock(x, y);
                    return;
            }

            ApplyDangerAnimationIfNeeded(x, y, block);
        }

        private void ApplyDangerAnimationIfNeeded(int x, int y, PanelBlock block)
        {
            if (block.Type == BlockType.Empty)
                return;

            if (block.State == PanelBlockState.Clear)
                return;

            if (block.AnimState == PanelBlockAnim.Land)
                return;

            if (IsDangerColumnBlock(x))
            {
                block.AnimState = PanelBlockAnim.Danger;
            }
            else if (block.AnimState == PanelBlockAnim.Danger)
            {
                block.AnimState = PanelBlockAnim.None;
            }
        }

        private bool IsDangerColumnBlock(int x)
        {
            int warningHeight = 2;
            for (int y = Height - 1; y > (Height - 1) - warningHeight; y--)
            {
                if (Grid[x, y].Type != BlockType.Empty)
                    return true;
            }
            return false;
        }

        private void FallOneCell(int x, int y)
        {
            PanelBlock src = Grid[x, y];
            PanelBlock dst = Grid[x, y - 1];

            dst.Type = src.Type;
            dst.State = src.State;
            dst.Counter = src.Counter;
            dst.Chain = src.Chain;
            dst.AnimState = src.AnimState;
            dst.AnimCounter = src.AnimCounter;
            dst.ExplodeCounter = src.ExplodeCounter;
            dst.ClearComboSize = src.ClearComboSize;

            src.Type = BlockType.Empty;
            src.State = PanelBlockState.Static;
            src.Counter = 0;
            src.Chain = false;
            src.AnimState = PanelBlockAnim.None;
            src.AnimCounter = 0;
            src.ExplodeCounter = 0;
            src.ClearComboSize = 0;
        }

        private void EraseBlock(int x, int y)
        {
            PanelBlock block = Grid[x, y];

            if (_activeClearSoundStep < _activeClearSoundMaxSteps)
            {
                _activeClearSoundStep++;

                if (PanelPonPlugin.Instance != null)
                    PanelPonPlugin.Instance.PlayChainStepSfx(_activeClearSoundChain, _activeClearSoundStep);
            }

            block.Type = BlockType.Empty;
            block.State = PanelBlockState.Static;
            block.Counter = 0;
            block.Chain = false;
            block.AnimState = PanelBlockAnim.None;
            block.AnimCounter = 0;
            block.ExplodeCounter = 0;
            block.ClearComboSize = 0;

            if (y + 1 < Height && Grid[x, y + 1].Type != BlockType.Empty)
                Grid[x, y + 1].Chain = true;
        }

        private ClearResult UpdateCombosAndChains()
        {
            List<Vector2Int> combo = new List<Vector2Int>();
            bool chain = false;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (TryMarkComboAt(x, y, combo))
                        chain = chain || Grid[x, y].Chain;
                }
            }

            combo.Sort((a, b) =>
            {
                if (a.y < b.y) return 1;
                if (a.y > b.y) return -1;
                if (a.x > b.x) return 1;
                if (a.x < b.x) return -1;
                return 0;
            });

            int comboCount = combo.Count;

            if (comboCount == 0)
                return new ClearResult(0, false);

            const int CLEAR_FACE_TIME = 18;
            const int CLEAR_HIGHLIGHT_TIME = 10;

            for (int i = combo.Count - 1; i >= 0; i--)
            {
                Vector2Int p = combo[i];
                PanelBlock block = Grid[p.x, p.y];

                block.State = PanelBlockState.Clear;
                block.ClearComboSize = comboCount;
                block.ExplodeCounter = (i + 1) * CLEAREXPLODETIME;

                int extraHighlight = comboCount > 3 ? CLEAR_HIGHLIGHT_TIME : 0;
                block.Counter = block.ExplodeCounter + CLEAR_FACE_TIME + extraHighlight;

                block.AnimState = comboCount > 3
                    ? PanelBlockAnim.ClearHighlight
                    : PanelBlockAnim.ClearFace;

                block.AnimCounter = extraHighlight > 0 ? CLEAR_HIGHLIGHT_TIME : CLEAR_FACE_TIME;
            }

            return new ClearResult(comboCount, chain);
        }

        private bool TryMarkComboAt(int x, int y, List<Vector2Int> combo)
        {
            if (!IsClearable(x, y))
                return false;

            bool chain = false;
            PanelBlock center = Grid[x, y];

            if (x > 0 && x < Width - 1)
            {
                if (IsClearable(x - 1, y) && IsClearable(x + 1, y))
                {
                    if (Grid[x - 1, y].Type == center.Type && Grid[x + 1, y].Type == center.Type)
                    {
                        AddUnique(combo, x - 1, y);
                        AddUnique(combo, x, y);
                        AddUnique(combo, x + 1, y);

                        if (Grid[x - 1, y].Chain || Grid[x, y].Chain || Grid[x + 1, y].Chain)
                            chain = true;
                    }
                }
            }

            if (y > 0 && y < Height - 1)
            {
                if (IsClearable(x, y - 1) && IsClearable(x, y + 1))
                {
                    if (Grid[x, y - 1].Type == center.Type && Grid[x, y + 1].Type == center.Type)
                    {
                        AddUnique(combo, x, y - 1);
                        AddUnique(combo, x, y);
                        AddUnique(combo, x, y + 1);

                        if (Grid[x, y - 1].Chain || Grid[x, y].Chain || Grid[x, y + 1].Chain)
                            chain = true;
                    }
                }
            }

            return chain;
        }

        private void AddUnique(List<Vector2Int> combo, int x, int y)
        {
            Vector2Int p = new Vector2Int(x, y);
            if (!combo.Contains(p))
                combo.Add(p);
        }

        private bool IsClearable(int x, int y)
        {
            PanelBlock block = Grid[x, y];
            if (block.Type == BlockType.Empty)
                return false;

            if (!IsSwappable(x, y))
                return false;

            if (y == 0)
                return true;

            return Grid[x, y - 1].IsSupport();
        }

        private bool IsSwappable(int x, int y)
        {
            if (y + 1 < Height && Grid[x, y + 1].State == PanelBlockState.Hang)
                return false;

            return Grid[x, y].Counter == 0;
        }

        private bool HasPendingChainBlocks()
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (Grid[x, y].Type != BlockType.Empty && Grid[x, y].Chain)
                        return true;
                }
            }
            return false;
        }

        private void AddScoreForClear(int comboCount, bool wasChain)
        {
            Score += comboCount * 10;
            Score += ComboToScore(comboCount);

            if (wasChain)
                CurrentChain++;
            else
                CurrentChain = 1;

            if (CurrentChain >= 2)
                Score += ChainToScore(CurrentChain);
        }

        private int ComboToScore(int combo)
        {
            switch (combo)
            {
                case 4: return 20;
                case 5: return 30;
                case 6: return 50;
                case 7: return 60;
                case 8: return 70;
                case 9: return 80;
                case 10: return 100;
                case 11: return 140;
                case 12: return 170;
                default: return 0;
            }
        }

        private int ChainToScore(int chain)
        {
            switch (chain)
            {
                case 2: return 50;
                case 3: return 80;
                case 4: return 150;
                case 5: return 300;
                case 6: return 400;
                case 7: return 500;
                case 8: return 700;
                case 9: return 900;
                case 10: return 1100;
                case 11: return 1300;
                case 12: return 1500;
                case 13: return 1800;
                default: return 0;
            }
        }

        private void Push()
        {
            for (int x = 0; x < Width; x++)
            {
                if (Grid[x, Height - 1].Type != BlockType.Empty)
                {
                    TriggerGameOver();
                    return;
                }
            }

            for (int y = Height - 1; y >= 1; y--)
            {
                for (int x = 0; x < Width; x++)
                {
                    CopyBlock(Grid[x, y - 1], Grid[x, y]);
                }
            }

            for (int x = 0; x < Width; x++)
                CopyBlock(NextLine[x], Grid[x, 0]);

            FillNextLine();

            CursorY = Mathf.Clamp(CursorY + 1, 0, Height - 1);
        }

        private void CopyBlock(PanelBlock src, PanelBlock dst)
        {
            dst.Type = src.Type;
            dst.State = src.State;
            dst.Counter = src.Counter;
            dst.AnimState = src.AnimState;
            dst.AnimCounter = src.AnimCounter;
            dst.ExplodeCounter = src.ExplodeCounter;
            dst.Chain = src.Chain;
            dst.ClearComboSize = src.ClearComboSize;
        }

        private void CheckGameOverImmediate()
        {
            for (int x = 0; x < Width; x++)
            {
                if (Grid[x, Height - 1].Type != BlockType.Empty)
                {
                    TriggerGameOver();
                    return;
                }
            }
        }

        private void TriggerGameOver()
        {
            if (IsGameOver)
                return;

            IsGameOver = true;
            GameOverThisTick = true;

            if (Score > PersonalBest)
                PersonalBest = Score;
        }

        private void ClearBoard()
        {
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                    Grid[x, y] = new PanelBlock();

                NextLine[x] = new PanelBlock();
            }
        }

        private void FillStartingRows(int rows)
        {
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Grid[x, y].Type = RandomBlockForStartGrid(x, y);
                    Grid[x, y].State = PanelBlockState.Static;
                    Grid[x, y].Counter = 0;
                    Grid[x, y].Chain = false;
                    Grid[x, y].AnimState = PanelBlockAnim.None;
                    Grid[x, y].AnimCounter = 0;
                    Grid[x, y].ExplodeCounter = 0;
                    Grid[x, y].ClearComboSize = 0;
                }
            }
        }

        private void FillNextLine()
        {
            for (int x = 0; x < Width; x++)
            {
                NextLine[x].Type = RandomBlockForNextLine(x);
                NextLine[x].State = PanelBlockState.Static;
                NextLine[x].Counter = 0;
                NextLine[x].Chain = false;
                NextLine[x].AnimState = PanelBlockAnim.None;
                NextLine[x].AnimCounter = 0;
                NextLine[x].ExplodeCounter = 0;
                NextLine[x].ClearComboSize = 0;
            }
        }

        private BlockType RandomBlockForStartGrid(int x, int y)
        {
            List<BlockType> candidates = GetAllBlockCandidates();

            if (x >= 2)
            {
                BlockType left1 = Grid[x - 1, y].Type;
                BlockType left2 = Grid[x - 2, y].Type;

                if (left1 != BlockType.Empty && left1 == left2)
                    candidates.Remove(left1);
            }

            if (y >= 2)
            {
                BlockType down1 = Grid[x, y - 1].Type;
                BlockType down2 = Grid[x, y - 2].Type;

                if (down1 != BlockType.Empty && down1 == down2)
                    candidates.Remove(down1);
            }

            return PickRandomCandidate(candidates);
        }

        private BlockType RandomBlockForNextLine(int x)
        {
            List<BlockType> candidates = GetAllBlockCandidates();

            if (x >= 2)
            {
                BlockType left1 = NextLine[x - 1].Type;
                BlockType left2 = NextLine[x - 2].Type;

                if (left1 != BlockType.Empty && left1 == left2)
                    candidates.Remove(left1);
            }

            if (Height >= 3)
            {
                BlockType above1 = Grid[x, 1].Type;
                BlockType above2 = Grid[x, 2].Type;

                if (above1 != BlockType.Empty && above1 == above2)
                    candidates.Remove(above1);
            }

            return PickRandomCandidate(candidates);
        }

        private List<BlockType> GetAllBlockCandidates()
        {
            return new List<BlockType>
            {
                BlockType.Red,
                BlockType.Blue,
                BlockType.Green,
                BlockType.Yellow,
                BlockType.Purple,
                BlockType.DarkBlue
            };
        }

        private BlockType PickRandomCandidate(List<BlockType> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return RandomBlock();

            return candidates[_rng.Next(candidates.Count)];
        }

        private BlockType RandomBlock()
        {
            return (BlockType)_rng.Next(1, 7);
        }
    }
}