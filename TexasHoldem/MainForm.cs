using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TexasHoldem
{
    /// <summary>
    /// The main game window. Renders a 6-handed Texas Hold'em table with custom drawing,
    /// drives the GameEngine, and routes human input via buttons + raise slider.
    /// </summary>
    public partial class MainForm : Form
    {
        private GameEngine _engine;
        private Random _rng = new Random();

        // UI controls
        private Panel _tablePanel;
        private Button _btnFold, _btnCheckCall, _btnRaise, _btnAllIn, _btnNewHand;
        private TrackBar _raiseBar;
        private Label _lblRaiseAmount;
        private CheckBox _chkSound;
        private CheckBox _chkVoice;
        private RichTextBox _log;
        private Timer _aiTimer;

        // State for animations / display
        private string _statusMessage = "Welcome to Texas Hold'em!";
        private List<ChipAnimation> _chipAnims = new List<ChipAnimation>();
        private Timer _animTimer;
        private List<Player> _showdownReveal = new List<Player>(); // players whose hole cards are face-up
        private List<Player> _lastHandWinners = new List<Player>();

        public MainForm()
        {
            InitializeComponentManually();
            // Defer game start until form is fully loaded — timers and button state
            // are unreliable inside the constructor.
            this.Shown += (s, e) => InitializeGame();
        }

        // --- Setup ---

        private void InitializeComponentManually()
        {
            this.Text = "德州撲克 Texas Hold'em - 1123305 范宸瑋";
            this.ClientSize = new Size(1280, 800);
            this.MinimumSize = new Size(1100, 720);
            this.BackColor = Color.FromArgb(25, 25, 30);
            this.DoubleBuffered = true;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Table panel (custom-drawn green felt)
            _tablePanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 60, 35)
            };
            _tablePanel.Paint += TablePanel_Paint;
            _tablePanel.Resize += (s, e) => _tablePanel.Invalidate();
            this.Controls.Add(_tablePanel);

            _log = new RichTextBox
            {
                Dock = DockStyle.Right,
                Width = 280,
                BackColor = Color.FromArgb(20, 20, 25),
                ForeColor = Color.LightGray,
                Font = new Font("Consolas", 9),
                ReadOnly = true,
                BorderStyle = BorderStyle.None
            };
            this.Controls.Add(_log);

            // Bottom action bar - added AFTER the log so docking gives it the correct width.
            var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = 110, BackColor = Color.FromArgb(35, 35, 40) };
            this.Controls.Add(bottomBar);

            _btnFold = MakeButton("Fold 棄牌", Color.FromArgb(180, 60, 60));
            _btnCheckCall = MakeButton("Check 過牌", Color.FromArgb(60, 130, 200));
            _btnRaise = MakeButton("Raise 加注", Color.FromArgb(220, 160, 30));
            _btnAllIn = MakeButton("All-In 全下", Color.FromArgb(170, 50, 170));
            _btnNewHand = MakeButton("New Hand 新局", Color.FromArgb(60, 150, 90));

            int x = 20;
            foreach (var b in new[] { _btnFold, _btnCheckCall, _btnRaise, _btnAllIn })
            {
                b.Location = new Point(x, 15);
                b.Size = new Size(140, 60);
                bottomBar.Controls.Add(b);
                x += 150;
            }

            _raiseBar = new TrackBar
            {
                Location = new Point(620, 15),
                Size = new Size(360, 50),
                Minimum = 0,
                Maximum = 1000,
                TickStyle = TickStyle.None,
                BackColor = Color.FromArgb(35, 35, 40)
            };
            _raiseBar.Scroll += (s, e) => UpdateRaiseLabel();
            bottomBar.Controls.Add(_raiseBar);

            _lblRaiseAmount = new Label
            {
                Location = new Point(620, 60),
                Size = new Size(360, 25),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Raise to: 0"
            };
            bottomBar.Controls.Add(_lblRaiseAmount);

            _btnNewHand.Size = new Size(140, 60);
            _btnNewHand.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            // Place near right edge of bottom bar; will stay anchored on resize
            _btnNewHand.Location = new Point(bottomBar.Width - _btnNewHand.Width - 20, 15);
            bottomBar.Controls.Add(_btnNewHand);

            _chkSound = new CheckBox
            {
                Size = new Size(70, 24),
                Text = "音效",
                ForeColor = Color.White,
                Checked = true,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _chkSound.Location = new Point(bottomBar.Width - 155, 80);
            _chkSound.CheckedChanged += (s, e) => SoundManager.Enabled = _chkSound.Checked;
            bottomBar.Controls.Add(_chkSound);

            _chkVoice = new CheckBox
            {
                Size = new Size(70, 24),
                Text = "語音",
                ForeColor = Color.White,
                Checked = true,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            _chkVoice.Location = new Point(bottomBar.Width - 80, 80);
            _chkVoice.CheckedChanged += (s, e) => SpeechManager.Enabled = _chkVoice.Checked;
            bottomBar.Controls.Add(_chkVoice);

            _btnFold.Click += (s, e) => HumanAct(PlayerAction.Fold);
            _btnCheckCall.Click += (s, e) =>
            {
                var info = _engine.GetActionInfo();
                HumanAct(info.canCheck ? PlayerAction.Check : PlayerAction.Call);
            };
            _btnRaise.Click += (s, e) => HumanAct(PlayerAction.Raise, _raiseBar.Value);
            _btnAllIn.Click += (s, e) => HumanAct(PlayerAction.AllIn);
            _btnNewHand.Click += (s, e) => StartNextHand();

            _aiTimer = new Timer { Interval = 900 };
            _aiTimer.Tick += AiTimer_Tick;

            _animTimer = new Timer { Interval = 30 };
            _animTimer.Tick += (s, e) =>
            {
                bool changed = false;
                for (int i = _chipAnims.Count - 1; i >= 0; i--)
                {
                    _chipAnims[i].Step();
                    if (_chipAnims[i].Done) _chipAnims.RemoveAt(i);
                    changed = true;
                }
                if (changed) _tablePanel.Invalidate();
            };
            _animTimer.Start();
        }

        private Button MakeButton(string text, Color color)
        {
            var b = new Button
            {
                Text = text,
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void InitializeGame()
        {
            _engine = new GameEngine();
            _engine.OnEvent += Engine_OnEvent;

            _engine.AddPlayer(new Player(0, "You 玩家", true, 1000));
            string[] aiNames = { "Alex", "Bella", "Carlos", "Dora", "Eric" };
            for (int i = 0; i < 5; i++)
                _engine.AddPlayer(new Player(i + 1, aiNames[i], false, 1000));

            StartNextHand();
        }

        // --- Engine event handling ---

        private void Engine_OnEvent(GameEvent e)
        {
            switch (e.Type)
            {
                case GameEvent.Kind.HandStart:
                    _showdownReveal.Clear();
                    _lastHandWinners.Clear();
                    Log($"--- 新一局開始 ---", Color.Yellow);
                    SoundManager.Play("shuffle");
                    SpeechManager.Speak("新的一局，發牌");
                    _statusMessage = "Hand started.";
                    break;
                case GameEvent.Kind.BlindsPosted:
                    Log(e.Message, Color.LightSkyBlue);
                    AnimateBlindChips();
                    SoundManager.Play("chip");
                    break;
                case GameEvent.Kind.HoleCardsDealt:
                    Log("Hole cards dealt", Color.LightGray);
                    SoundManager.Play("deal");
                    break;
                case GameEvent.Kind.FlopDealt:
                case GameEvent.Kind.TurnDealt:
                case GameEvent.Kind.RiverDealt:
                    Log(e.Message + ": " + string.Join(" ", e.Cards.Select(c => c.ImageKey)), Color.LightYellow);
                    SoundManager.Play("deal");
                    break;
                case GameEvent.Kind.PlayerActed:
                    {
                        string actionText = ActionText(e.Player.LastAction, e.Player.CurrentBet);
                        Log($"{e.Player.Name}: {actionText}", e.Player.IsHuman ? Color.White : Color.LightGreen);
                        _statusMessage = $"{e.Player.Name} → {actionText}";
                        // TTS announcement for every action
                        SpeechManager.SpeakAction(e.Player.Name, e.Player.LastAction, e.Player.CurrentBet);
                        switch (e.Player.LastAction)
                        {
                            case PlayerAction.Fold: SoundManager.Play("fold"); break;
                            case PlayerAction.Check: SoundManager.Play("check"); break;
                            case PlayerAction.AllIn:
                                SoundManager.Play("allin");
                                AnimateChipsToPot(e.Player);
                                break;
                            default:
                                SoundManager.Play("chip");
                                AnimateChipsToPot(e.Player);
                                break;
                        }
                    }
                    break;
                case GameEvent.Kind.Showdown:
                    foreach (var p in _engine.Players)
                        if (!p.HasFolded) _showdownReveal.Add(p);
                    break;
                case GameEvent.Kind.PotAwarded:
                    if (e.Pots != null)
                    {
                        foreach (var pot in e.Pots)
                        {
                            string wins = string.Join(", ", pot.Winners.Select(w => w.Name));
                            Log($"{wins} wins {pot.Amount} ({pot.HandDescription})", Color.Gold);
                            _lastHandWinners.AddRange(pot.Winners);
                            // TTS for winner
                            string winnerSpoken = pot.Winners.Count == 1
                                ? pot.Winners[0].Name.Replace(" 玩家", "")
                                : "多位玩家";
                            SpeechManager.Speak($"{winnerSpoken} 贏得 {pot.Amount} 籌碼");
                        }
                    }
                    break;
                case GameEvent.Kind.HandEnded:
                    {
                        bool humanWon = _lastHandWinners.Any(p => p.IsHuman);
                        SoundManager.Play(humanWon ? "win" : "lose");
                        _statusMessage = "Hand ended. Click 'New Hand' to play another.";
                        UpdateActionButtonsForBetweenHands();
                    }
                    break;
                case GameEvent.Kind.GameOver:
                    Log("Game over: " + e.Message, Color.OrangeRed);
                    UpdateActionButtonsForBetweenHands();
                    break;
            }

            _tablePanel.Invalidate();

            // If it's an AI's turn, run them via timer
            if (e.Type == GameEvent.Kind.HoleCardsDealt
                || e.Type == GameEvent.Kind.PlayerActed
                || e.Type == GameEvent.Kind.FlopDealt
                || e.Type == GameEvent.Kind.TurnDealt
                || e.Type == GameEvent.Kind.RiverDealt
                || e.Type == GameEvent.Kind.BettingRoundEnded)
            {
                ScheduleNextActor();
            }
        }

        private string ActionText(PlayerAction a, int bet)
        {
            switch (a)
            {
                case PlayerAction.Fold: return "Fold";
                case PlayerAction.Check: return "Check";
                case PlayerAction.Call: return $"Call to {bet}";
                case PlayerAction.Raise: return $"Raise to {bet}";
                case PlayerAction.AllIn: return $"All-In ({bet})";
                default: return a.ToString();
            }
        }

        private void StartNextHand()
        {
            // Remove broke players (chips==0)? Or end game.
            int alive = _engine.Players.Count(p => p.Chips > 0);
            if (alive < 2)
            {
                MessageBox.Show("Game over. " + _engine.Players.OrderByDescending(p => p.Chips).First().Name + " is the chip leader!",
                    "Game Over", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // Reset all chips
                foreach (var p in _engine.Players) p.Chips = 1000;
            }
            _engine.StartNewHand();
        }

        // --- AI scheduling ---

        private void ScheduleNextActor()
        {
            var actor = _engine.ActingPlayer;
            if (actor == null || _engine.CurrentStreet == Street.HandOver) { UpdateActionButtonsForBetweenHands(); return; }
            if (actor.IsHuman)
            {
                _aiTimer.Stop();
                UpdateActionButtonsForHuman();
            }
            else
            {
                UpdateActionButtonsDisabled();
                _aiTimer.Stop();
                _aiTimer.Start();
            }
        }

        private void AiTimer_Tick(object sender, EventArgs e)
        {
            _aiTimer.Stop();
            var actor = _engine.ActingPlayer;
            if (actor == null || actor.IsHuman) return;
            var info = _engine.GetActionInfo();
            var decision = AiBrain.Decide(actor, _engine.Community, info.callAmount, _engine.MinRaise, _engine.Pot, _rng);
            try
            {
                _engine.Act(decision.Action, decision.Amount);
            }
            catch (Exception ex)
            {
                Log("AI error: " + ex.Message, Color.Red);
            }
        }

        // --- Human input ---

        private void HumanAct(PlayerAction action, int amount = 0)
        {
            var actor = _engine.ActingPlayer;
            if (actor == null || !actor.IsHuman) return;
            try
            {
                _engine.Act(action, amount);
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message, Color.Red);
            }
        }

        private void UpdateActionButtonsForHuman()
        {
            var p = _engine.ActingPlayer;
            if (p == null) { UpdateActionButtonsDisabled(); return; }
            var info = _engine.GetActionInfo();
            _btnFold.Enabled = true;
            _btnCheckCall.Enabled = true;
            _btnCheckCall.Text = info.canCheck ? "Check 過牌" : $"Call 跟注 ({info.callAmount})";
            _btnRaise.Enabled = info.maxRaiseTotal > p.CurrentBet && info.maxRaiseTotal > info.minRaiseTotal;
            _btnAllIn.Enabled = p.Chips > 0;
            _btnNewHand.Enabled = false;
            _raiseBar.Enabled = _btnRaise.Enabled;
            if (_btnRaise.Enabled)
            {
                _raiseBar.Minimum = info.minRaiseTotal;
                _raiseBar.Maximum = info.maxRaiseTotal;
                _raiseBar.Value = Math.Min(_raiseBar.Maximum, Math.Max(_raiseBar.Minimum, info.minRaiseTotal));
                UpdateRaiseLabel();
            }
            else
            {
                _lblRaiseAmount.Text = "Raise not available";
            }
        }

        private void UpdateActionButtonsDisabled()
        {
            _btnFold.Enabled = false;
            _btnCheckCall.Enabled = false;
            _btnRaise.Enabled = false;
            _btnAllIn.Enabled = false;
            _btnNewHand.Enabled = false;
            _raiseBar.Enabled = false;
        }

        private void UpdateActionButtonsForBetweenHands()
        {
            UpdateActionButtonsDisabled();
            _btnNewHand.Enabled = true;
        }

        private void UpdateRaiseLabel()
        {
            _lblRaiseAmount.Text = $"Raise to: {_raiseBar.Value}";
        }

        // --- Logging ---

        private void Log(string msg, Color color)
        {
            if (_log.InvokeRequired) { _log.BeginInvoke(new Action(() => Log(msg, color))); return; }
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color;
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine);
            _log.SelectionColor = _log.ForeColor;
            _log.ScrollToCaret();
        }

        // --- Drawing the table ---

        private Point[] _seatCenters = new Point[6];

        // For each seat, whether the hole cards are drawn above the name plate (true)
        // or below it (false). Top-row seats use 'below' so cards stay on the table felt.
        private bool[] _seatCardsAbove = new bool[6];

        private void ComputeSeatPositions()
        {
            // Fixed anchor positions tuned so cards and chips never clip off-screen
            // and don't overlap the community-card row in the middle of the table.
            int w = _tablePanel.Width, h = _tablePanel.Height;
            int cx = w / 2;
            int topY  = (int)(h * 0.13);  // plate y for top row (cards drawn below)
            int midY  = (int)(h * 0.46);  // mid rail (cards above plate)
            int botY  = (int)(h * 0.80);  // bottom (cards above plate)
            int xLeft  = (int)(w * 0.10);
            int xRight = (int)(w * 0.90);
            int xMidL  = (int)(w * 0.22);
            int xMidR  = (int)(w * 0.78);

            _seatCenters[0] = new Point(cx,      botY);          _seatCardsAbove[0] = true;   // you (bottom)
            _seatCenters[1] = new Point(xMidR,   botY);          _seatCardsAbove[1] = true;   // bottom-right
            _seatCenters[2] = new Point(xRight,  midY);          _seatCardsAbove[2] = true;   // mid-right
            _seatCenters[3] = new Point(cx,      topY);          _seatCardsAbove[3] = false;  // top center
            _seatCenters[4] = new Point(xLeft,   midY);          _seatCardsAbove[4] = true;   // mid-left
            _seatCenters[5] = new Point(xMidL,   botY);          _seatCardsAbove[5] = true;   // bottom-left
        }

        private void TablePanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            ComputeSeatPositions();

            DrawTable(g);
            DrawCommunity(g);
            DrawPotAndStatus(g);
            DrawSeats(g);
            DrawChipAnimations(g);
        }

        private void DrawTable(Graphics g)
        {
            int w = _tablePanel.Width, h = _tablePanel.Height;
            // Outer table felt
            var tableRect = new Rectangle((int)(w * 0.08), (int)(h * 0.12), (int)(w * 0.84), (int)(h * 0.76));
            using (var path = RoundedRect(tableRect, 120))
            {
                using (var brush = new LinearGradientBrush(tableRect, Color.FromArgb(35, 110, 60), Color.FromArgb(10, 55, 30), LinearGradientMode.Vertical))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(70, 40, 20), 12))
                    g.DrawPath(pen, path);
                using (var pen = new Pen(Color.FromArgb(140, 95, 50), 3))
                    g.DrawPath(pen, path);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void DrawCommunity(Graphics g)
        {
            int cardW = 80, cardH = 112;
            int gap = 12;
            int totalW = 5 * cardW + 4 * gap;
            int x = (_tablePanel.Width - totalW) / 2;
            int y = _tablePanel.Height / 2 - cardH / 2 - 30;

            for (int i = 0; i < 5; i++)
            {
                var rect = new Rectangle(x + i * (cardW + gap), y, cardW, cardH);
                if (i < _engine.Community.Count)
                    g.DrawImage(CardImageProvider.GetCard(_engine.Community[i]), rect);
                else
                {
                    using (var br = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                        g.FillRectangle(br, rect);
                    using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1))
                        g.DrawRectangle(pen, rect);
                }
            }
        }

        private void DrawPotAndStatus(Graphics g)
        {
            int y = _tablePanel.Height / 2 + 40;
            string potText = $"Pot 底池: {_engine.Pot}";
            using (var font = new Font("Segoe UI", 16, FontStyle.Bold))
            {
                var size = g.MeasureString(potText, font);
                float px = (_tablePanel.Width - size.Width) / 2;
                using (var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                    g.FillRectangle(bg, px - 10, y - 4, size.Width + 20, size.Height + 8);
                using (var br = new SolidBrush(Color.Gold))
                    g.DrawString(potText, font, br, px, y);
            }

            // Status line at top
            using (var font = new Font("Segoe UI", 11))
            using (var br = new SolidBrush(Color.White))
            {
                g.DrawString(_statusMessage, font, br, 20, 8);
            }

            // Street label
            string street = _engine.CurrentStreet.ToString();
            using (var font = new Font("Segoe UI", 11, FontStyle.Italic))
            using (var br = new SolidBrush(Color.LightYellow))
            {
                g.DrawString("Street: " + street, font, br, _tablePanel.Width - 220, 8);
            }
        }

        private void DrawSeats(Graphics g)
        {
            for (int i = 0; i < _engine.Players.Count; i++)
            {
                var p = _engine.Players[i];
                var center = _seatCenters[i];
                bool cardsAbove = i < _seatCardsAbove.Length ? _seatCardsAbove[i] : true;
                DrawSeat(g, p, center, i == _engine.DealerIndex, _engine.ActingPlayer == p, cardsAbove);
            }
        }

        private void DrawSeat(Graphics g, Player p, Point center, bool isDealer, bool isActing, bool cardsAbove)
        {
            // Plate placement depends on whether cards go above or below
            int plateW = 200, plateH = 60;
            int plateY = cardsAbove ? center.Y + 5 : center.Y + 5;
            var plate = new Rectangle(center.X - plateW / 2, plateY, plateW, plateH);

            // Hole cards
            int cardW = 60, cardH = 84;
            int cardGap = 8;
            int cardsTotal = cardW * 2 + cardGap;
            int cardX = center.X - cardsTotal / 2;
            int cardY = cardsAbove
                ? center.Y - cardH - 30        // above plate (default)
                : plate.Bottom + 12;           // below plate (for top-row seat)

            bool revealHole = p.IsHuman || _showdownReveal.Contains(p);
            if (p.Hole.Count == 2 && !p.HasFolded)
            {
                for (int i = 0; i < 2; i++)
                {
                    var rect = new Rectangle(cardX + i * (cardW + cardGap), cardY, cardW, cardH);
                    Image img = revealHole ? CardImageProvider.GetCard(p.Hole[i]) : CardImageProvider.GetBack();
                    g.DrawImage(img, rect);
                }
            }
            else if (p.HasFolded)
            {
                // Show faded box where cards would have been
                using (var br = new SolidBrush(Color.FromArgb(80, 50, 50, 50)))
                    g.FillRectangle(br, cardX, cardY, cardsTotal, cardH);
            }

            // Player name plate (rendered after cards so border is on top if overlapping)
            // (plate variable already declared above)
            bool isWinner = _lastHandWinners.Contains(p);
            Color plateColor = p.HasFolded
                ? Color.FromArgb(80, 60, 60, 60)
                : (isActing ? Color.FromArgb(220, 80, 50, 20) : Color.FromArgb(220, 25, 25, 35));
            using (var path = RoundedRect(plate, 14))
            {
                using (var br = new SolidBrush(plateColor))
                    g.FillPath(br, path);
                Color borderColor = isWinner ? Color.Gold : (isActing ? Color.Orange : Color.FromArgb(150, 200, 200, 200));
                int borderWidth = (isActing || isWinner) ? 3 : 1;
                using (var pen = new Pen(borderColor, borderWidth))
                    g.DrawPath(pen, path);
            }

            using (var nameFont = new Font("Segoe UI", 10, FontStyle.Bold))
            using (var infoFont = new Font("Segoe UI", 9))
            using (var br = new SolidBrush(Color.White))
            {
                g.DrawString(p.Name, nameFont, br, plate.X + 10, plate.Y + 6);
                g.DrawString($"Chips: {p.Chips}", infoFont, br, plate.X + 10, plate.Y + 26);
                if (p.LastAction != PlayerAction.None && !p.HasFolded)
                    g.DrawString(ActionText(p.LastAction, p.CurrentBet), infoFont, br, plate.X + 10, plate.Y + 42);
                else if (p.HasFolded)
                {
                    using (var fbr = new SolidBrush(Color.LightCoral))
                        g.DrawString("Folded", infoFont, fbr, plate.X + 10, plate.Y + 42);
                }
            }

            // Dealer button
            if (isDealer)
            {
                int btnSize = 30;
                int dx = center.X + 90, dy = center.Y - 10;
                using (var br = new SolidBrush(Color.White))
                    g.FillEllipse(br, dx, dy, btnSize, btnSize);
                using (var pen = new Pen(Color.Black, 2))
                    g.DrawEllipse(pen, dx, dy, btnSize, btnSize);
                using (var font = new Font("Segoe UI", 12, FontStyle.Bold))
                using (var br = new SolidBrush(Color.Black))
                {
                    var sz = g.MeasureString("D", font);
                    g.DrawString("D", font, br, dx + (btnSize - sz.Width) / 2, dy + (btnSize - sz.Height) / 2);
                }
            }

            // Current bet chip stack — drawn on the felt side of the seat (between seat and pot)
            if (p.CurrentBet > 0 && !p.HasFolded)
            {
                int chipX = center.X;
                int chipY = cardsAbove ? center.Y - 80 : plate.Bottom + cardH + 30;
                DrawChipStack(g, chipX, chipY, p.CurrentBet);
            }
        }

        private void DrawChipStack(Graphics g, int cx, int cy, int amount)
        {
            int stackHeight = Math.Min(5, 1 + amount / 100);
            for (int i = 0; i < stackHeight; i++)
            {
                var rect = new Rectangle(cx - 18, cy - i * 4, 36, 12);
                Color top = i % 2 == 0 ? Color.IndianRed : Color.SteelBlue;
                using (var br = new SolidBrush(top))
                    g.FillEllipse(br, rect);
                using (var pen = new Pen(Color.Black, 1))
                    g.DrawEllipse(pen, rect);
            }
            using (var font = new Font("Segoe UI", 9, FontStyle.Bold))
            using (var br = new SolidBrush(Color.White))
            using (var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
            {
                string txt = amount.ToString();
                var sz = g.MeasureString(txt, font);
                g.FillRectangle(bg, cx - sz.Width / 2 - 4, cy + 8, sz.Width + 8, sz.Height + 2);
                g.DrawString(txt, font, br, cx - sz.Width / 2, cy + 8);
            }
        }

        // --- Animations ---

        private class ChipAnimation
        {
            public PointF Pos;
            public PointF Target;
            public int Steps;
            public int Step_;
            public int Amount;
            public bool Done => Step_ >= Steps;
            public void Step() { Step_++; }
            public PointF Current()
            {
                float t = Math.Min(1f, (float)Step_ / Steps);
                return new PointF(Pos.X + (Target.X - Pos.X) * t, Pos.Y + (Target.Y - Pos.Y) * t);
            }
        }

        private void AnimateChipsToPot(Player p)
        {
            int seat = _engine.Players.IndexOf(p);
            if (seat < 0 || seat >= _seatCenters.Length) return;
            bool cardsAbove = seat < _seatCardsAbove.Length ? _seatCardsAbove[seat] : true;
            int dy = cardsAbove ? -60 : 130; // top seat: start below plate, others: above
            var from = new PointF(_seatCenters[seat].X, _seatCenters[seat].Y + dy);
            var to = new PointF(_tablePanel.Width / 2f, _tablePanel.Height / 2f + 60);
            _chipAnims.Add(new ChipAnimation { Pos = from, Target = to, Steps = 15, Amount = p.CurrentBet });
        }

        private void AnimateBlindChips()
        {
            // Generic chip clinks
            foreach (var p in _engine.Players.Where(pl => pl.CurrentBet > 0))
                AnimateChipsToPot(p);
        }

        private void DrawChipAnimations(Graphics g)
        {
            foreach (var a in _chipAnims)
            {
                var pt = a.Current();
                using (var br = new SolidBrush(Color.Gold))
                    g.FillEllipse(br, pt.X - 10, pt.Y - 5, 20, 10);
                using (var pen = new Pen(Color.Black, 1))
                    g.DrawEllipse(pen, pt.X - 10, pt.Y - 5, 20, 10);
            }
        }
    }

    /// <summary>
    /// Panel subclass with double buffering enabled to eliminate flicker.
    /// </summary>
    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }
    }
}
