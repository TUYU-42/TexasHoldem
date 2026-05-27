using System;
using System.Collections.Generic;
using System.Linq;

namespace TexasHoldem
{
    public enum Street { Preflop, Flop, Turn, River, Showdown, HandOver }

    /// <summary>
    /// Event payloads emitted by the engine so the UI can react/animate.
    /// </summary>
    public class GameEvent
    {
        public enum Kind
        {
            HandStart, BlindsPosted, HoleCardsDealt, FlopDealt, TurnDealt, RiverDealt,
            PlayerActed, BettingRoundEnded, Showdown, PotAwarded, HandEnded, GameOver
        }
        public Kind Type;
        public string Message;
        public Player Player;       // who acted (if relevant)
        public int Amount;          // amount associated (bet/pot)
        public List<Card> Cards;    // community/showdown cards
        public List<PotResult> Pots;
    }

    public class PotResult
    {
        public int Amount;
        public List<Player> Winners = new List<Player>();
        public string HandDescription; // best hand description (winner)
    }

    /// <summary>
    /// Drives a No-Limit Texas Hold'em hand for up to 6 players, including blinds, betting streets,
    /// side pots, and showdown. UI subscribes to events.
    /// </summary>
    public class GameEngine
    {
        public List<Player> Players { get; } = new List<Player>();
        public List<Card> Community { get; } = new List<Card>();
        public int Pot => Players.Sum(p => p.TotalCommitted);
        public int SmallBlind { get; set; } = 10;
        public int BigBlind { get; set; } = 20;
        public int DealerIndex { get; private set; }
        public Street CurrentStreet { get; private set; } = Street.HandOver;
        public int CurrentBet { get; private set; }
        public int MinRaise { get; private set; }
        public int LastAggressorIndex { get; private set; } = -1;
        public Player ActingPlayer => _actingIndex >= 0 && _actingIndex < Players.Count ? Players[_actingIndex] : null;

        public event Action<GameEvent> OnEvent;

        private Deck _deck;
        private Random _rng;
        private int _actingIndex = -1;

        public GameEngine(int seed = 0)
        {
            _rng = seed == 0 ? new Random() : new Random(seed);
            _deck = new Deck(_rng);
        }

        public void AddPlayer(Player p) => Players.Add(p);

        private void Emit(GameEvent e) => OnEvent?.Invoke(e);

        // --- Hand lifecycle ---

        public void StartNewHand()
        {
            // Eliminate broke players (chips==0 and not all-in) before starting a new hand
            foreach (var p in Players) p.NewHand();
            Community.Clear();
            _deck.Reset();
            CurrentBet = 0;
            MinRaise = BigBlind;
            LastAggressorIndex = -1;
            CurrentStreet = Street.Preflop;
            _actingIndex = -1;

            // Move dealer button
            int activeCount = Players.Count(p => p.Chips > 0);
            if (activeCount < 2)
            {
                Emit(new GameEvent { Type = GameEvent.Kind.GameOver, Message = "Not enough players to continue." });
                return;
            }
            DealerIndex = NextActiveIndex(DealerIndex); // advance to next eligible dealer

            Emit(new GameEvent { Type = GameEvent.Kind.HandStart, Message = "New hand started" });

            PostBlinds();
            DealHoleCards();

            // First to act preflop is UTG (left of BB). With <=3 active players, dealer acts first preflop.
            // CRITICAL: Set _actingIndex BEFORE emitting events so UI sees the correct acting player.
            int sb = NextActiveIndex(DealerIndex);
            int bb = NextActiveIndex(sb);
            _actingIndex = activeCount > 2 ? NextActiveIndex(bb) : DealerIndex;
            // Skip players who can't act
            int safety = 0;
            while (safety++ < Players.Count && _actingIndex >= 0 && !Players[_actingIndex].CanAct)
                _actingIndex = NextActiveIndex(_actingIndex);

            // Now that _actingIndex is set, fire a synthetic event to wake up the UI to schedule actor
            Emit(new GameEvent { Type = GameEvent.Kind.HoleCardsDealt, Message = "Ready for first action" });
        }

        private void PostBlinds()
        {
            int activeCount = Players.Count(p => p.Chips > 0);
            int sbIdx = NextActiveIndex(DealerIndex);
            int bbIdx = NextActiveIndex(sbIdx);
            // Heads-up special case: dealer = SB
            if (activeCount == 2)
            {
                sbIdx = DealerIndex;
                bbIdx = NextActiveIndex(DealerIndex);
            }

            Players[sbIdx].Commit(SmallBlind);
            Players[bbIdx].Commit(BigBlind);
            CurrentBet = BigBlind;
            MinRaise = BigBlind;
            LastAggressorIndex = bbIdx;
            Emit(new GameEvent
            {
                Type = GameEvent.Kind.BlindsPosted,
                Message = $"{Players[sbIdx].Name} posts SB {SmallBlind}, {Players[bbIdx].Name} posts BB {BigBlind}"
            });
        }

        private void DealHoleCards()
        {
            // Two passes, mimicking real dealing
            for (int pass = 0; pass < 2; pass++)
            {
                int i = NextActiveIndex(DealerIndex);
                int start = i;
                do
                {
                    if (Players[i].Chips + Players[i].CurrentBet > 0)
                        Players[i].Hole.Add(_deck.Draw());
                    i = NextActiveIndex(i);
                } while (i != start);
            }
            // Note: HoleCardsDealt event is emitted by StartNewHand AFTER _actingIndex is set,
            // so the UI knows whose turn it is when it processes the event.
        }

        // --- Action handling ---

        /// <summary>
        /// Returns the legal actions and amounts for the current acting player.
        /// </summary>
        public (bool canCheck, int callAmount, int minRaiseTotal, int maxRaiseTotal) GetActionInfo()
        {
            var p = ActingPlayer;
            if (p == null) return (false, 0, 0, 0);
            int toCall = Math.Max(0, CurrentBet - p.CurrentBet);
            bool canCheck = toCall == 0;
            int minRaiseTotal = CurrentBet + MinRaise;
            int maxRaiseTotal = p.CurrentBet + p.Chips;
            if (minRaiseTotal > maxRaiseTotal) minRaiseTotal = maxRaiseTotal;
            return (canCheck, toCall, minRaiseTotal, maxRaiseTotal);
        }

        /// <summary>
        /// Apply an action from the current acting player. amount = total bet target for Raise.
        /// </summary>
        public void Act(PlayerAction action, int amount = 0)
        {
            // Hard guard: refuse all actions once a hand is over or in showdown.
            if (CurrentStreet == Street.HandOver || CurrentStreet == Street.Showdown) return;
            var p = ActingPlayer;
            if (p == null) return;
            // Player must be able to act
            if (p.HasFolded || p.IsAllIn || p.Chips <= 0) return;

            switch (action)
            {
                case PlayerAction.Fold:
                    p.HasFolded = true;
                    p.LastAction = PlayerAction.Fold;
                    break;
                case PlayerAction.Check:
                    p.LastAction = PlayerAction.Check;
                    break;
                case PlayerAction.Call:
                    {
                        int toCall = Math.Max(0, CurrentBet - p.CurrentBet);
                        int committed = p.Commit(toCall);
                        p.LastAction = p.IsAllIn ? PlayerAction.AllIn : PlayerAction.Call;
                    }
                    break;
                case PlayerAction.Raise:
                    {
                        int target = Math.Max(amount, CurrentBet + MinRaise);
                        target = Math.Min(target, p.CurrentBet + p.Chips);
                        int delta = target - p.CurrentBet;
                        int committed = p.Commit(delta);
                        int raiseSize = (p.CurrentBet) - CurrentBet;
                        if (raiseSize >= MinRaise) MinRaise = raiseSize;
                        if (p.CurrentBet > CurrentBet)
                        {
                            CurrentBet = p.CurrentBet;
                            LastAggressorIndex = _actingIndex;
                            // Re-open action for everyone else who already acted
                            foreach (var other in Players)
                                if (other != p && !other.HasFolded && !other.IsAllIn) other.HasActed = false;
                        }
                        p.LastAction = p.IsAllIn ? PlayerAction.AllIn : PlayerAction.Raise;
                    }
                    break;
                case PlayerAction.AllIn:
                    {
                        int beforeBet = p.CurrentBet;
                        int committed = p.Commit(p.Chips);
                        if (p.CurrentBet > CurrentBet)
                        {
                            int raiseSize = p.CurrentBet - CurrentBet;
                            if (raiseSize >= MinRaise) MinRaise = raiseSize;
                            CurrentBet = p.CurrentBet;
                            LastAggressorIndex = _actingIndex;
                            foreach (var other in Players)
                                if (other != p && !other.HasFolded && !other.IsAllIn) other.HasActed = false;
                        }
                        p.LastAction = PlayerAction.AllIn;
                    }
                    break;
            }

            p.HasActed = true;

            // Emit PlayerActed FIRST so UI logs/animates the action while engine state is consistent.
            // (We set HasActed first so the engine state is correct when UI reads it.)
            // Then decide what happens next.

            // Case 1: only one non-folded left → hand ends immediately
            int notFolded = Players.Count(x => !x.HasFolded);
            if (notFolded <= 1)
            {
                Emit(new GameEvent { Type = GameEvent.Kind.PlayerActed, Player = p, Amount = p.CurrentBet, Message = $"{p.Name} {action}" });
                EndBettingRoundAndProceed();
                return;
            }

            // Case 2: try to find next actor. If none, the street/hand ends inside this call.
            bool streetEnded = !TryAdvanceAction();
            // Emit PlayerActed AFTER _actingIndex is settled (either to next actor, or HandOver if showdown ran).
            Emit(new GameEvent { Type = GameEvent.Kind.PlayerActed, Player = p, Amount = p.CurrentBet, Message = $"{p.Name} {action}" });

            if (streetEnded)
            {
                // End the betting round AFTER PlayerActed has been emitted, so UI processes them in order.
                EndBettingRoundAndProceed();
            }
        }

        /// <summary>
        /// Find the next player who can act and update _actingIndex.
        /// Returns true if a next actor was found, false if no one can act (caller should end the street).
        /// </summary>
        private bool TryAdvanceAction()
        {
            int start = _actingIndex;
            int i = NextActiveIndex(start);
            int safety = 0;
            while (safety++ < Players.Count + 1)
            {
                var p = Players[i];
                if (!p.HasFolded && !p.IsAllIn && p.Chips > 0 && (!p.HasActed || p.CurrentBet < CurrentBet))
                {
                    _actingIndex = i;
                    return true;
                }
                i = NextActiveIndex(i);
                if (i == start) break;
            }
            return false;
        }

        private void EndBettingRoundAndProceed()
        {
            Emit(new GameEvent { Type = GameEvent.Kind.BettingRoundEnded, Message = $"End of {CurrentStreet}" });

            int notFolded = Players.Count(p => !p.HasFolded);
            if (notFolded <= 1)
            {
                // Award immediately to last standing
                Showdown();
                return;
            }

            // If everyone else is all-in, deal remaining community cards then showdown
            int canStillBet = Players.Count(p => !p.HasFolded && !p.IsAllIn);
            bool runItOut = canStillBet <= 1;

            // Deal next street's cards (silent — events emitted after _actingIndex is set)
            GameEvent.Kind dealtEvent = GameEvent.Kind.FlopDealt;
            switch (CurrentStreet)
            {
                case Street.Preflop:
                    DealFlop();
                    CurrentStreet = Street.Flop;
                    dealtEvent = GameEvent.Kind.FlopDealt;
                    break;
                case Street.Flop:
                    DealTurn();
                    CurrentStreet = Street.Turn;
                    dealtEvent = GameEvent.Kind.TurnDealt;
                    break;
                case Street.Turn:
                    DealRiver();
                    CurrentStreet = Street.River;
                    dealtEvent = GameEvent.Kind.RiverDealt;
                    break;
                case Street.River:
                    Showdown();
                    return;
            }

            if (runItOut)
            {
                if (CurrentStreet == Street.Flop) { DealTurn(); CurrentStreet = Street.Turn; }
                if (CurrentStreet == Street.Turn) { DealRiver(); CurrentStreet = Street.River; }
                Showdown();
                return;
            }

            // Reset street state
            foreach (var p in Players) p.NewStreet();
            CurrentBet = 0;
            MinRaise = BigBlind;
            LastAggressorIndex = -1;

            // First to act post-flop = first active player left of dealer
            _actingIndex = NextActiveIndex(DealerIndex);
            int safety = 0;
            while (safety++ < Players.Count && !Players[_actingIndex].CanAct)
                _actingIndex = NextActiveIndex(_actingIndex);

            // Now emit the street-dealt event so UI schedules the correct actor
            Emit(new GameEvent { Type = dealtEvent, Cards = new List<Card>(Community), Message = CurrentStreet.ToString() });
        }

        private void DealFlop()
        {
            _deck.Draw(); // burn
            Community.Add(_deck.Draw());
            Community.Add(_deck.Draw());
            Community.Add(_deck.Draw());
        }
        private void DealTurn()
        {
            _deck.Draw();
            Community.Add(_deck.Draw());
        }
        private void DealRiver()
        {
            _deck.Draw();
            Community.Add(_deck.Draw());
        }

        // --- Showdown & side pots ---

        private void Showdown()
        {
            CurrentStreet = Street.Showdown;
            _actingIndex = -1; // no one is acting anymore
            var contenders = Players.Where(p => !p.HasFolded).ToList();

            var pots = BuildPots();
            var results = new List<PotResult>();

            foreach (var pot in pots)
            {
                var eligible = pot.Eligible.Where(p => !p.HasFolded).ToList();
                if (eligible.Count == 0) continue;

                if (eligible.Count == 1)
                {
                    eligible[0].Chips += pot.Amount;
                    results.Add(new PotResult { Amount = pot.Amount, Winners = new List<Player> { eligible[0] }, HandDescription = "Uncontested" });
                    continue;
                }

                // Evaluate hands among eligible
                HandValue best = null;
                var winners = new List<Player>();
                var evals = new Dictionary<Player, HandValue>();
                foreach (var p in eligible)
                {
                    var hv = HandEvaluator.Evaluate(p.Hole.Concat(Community));
                    evals[p] = hv;
                    if (best == null || hv.CompareTo(best) > 0)
                    {
                        best = hv;
                        winners.Clear();
                        winners.Add(p);
                    }
                    else if (hv.CompareTo(best) == 0)
                    {
                        winners.Add(p);
                    }
                }

                int share = pot.Amount / winners.Count;
                int remainder = pot.Amount - share * winners.Count;
                foreach (var w in winners) w.Chips += share;
                if (remainder > 0 && winners.Count > 0) winners[0].Chips += remainder;

                results.Add(new PotResult { Amount = pot.Amount, Winners = winners, HandDescription = best.Description });
            }

            Emit(new GameEvent { Type = GameEvent.Kind.Showdown, Cards = new List<Card>(Community) });
            Emit(new GameEvent { Type = GameEvent.Kind.PotAwarded, Pots = results });
            CurrentStreet = Street.HandOver;
            Emit(new GameEvent { Type = GameEvent.Kind.HandEnded, Pots = results });
        }

        private class PotSlice
        {
            public int Amount;
            public List<Player> Eligible = new List<Player>();
        }

        /// <summary>
        /// Build main + side pots from each player's TotalCommitted in this hand.
        /// </summary>
        private List<PotSlice> BuildPots()
        {
            var pots = new List<PotSlice>();
            // Distinct all-in amounts (and the max non-all-in committed) define pot boundaries
            var levels = Players
                .Where(p => p.TotalCommitted > 0)
                .Select(p => p.TotalCommitted)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            int prev = 0;
            foreach (var lvl in levels)
            {
                var contributors = Players.Where(p => p.TotalCommitted >= lvl).ToList();
                int sliceAmount = (lvl - prev) * contributors.Count;
                if (sliceAmount > 0)
                {
                    var eligible = contributors.Where(p => !p.HasFolded).ToList();
                    pots.Add(new PotSlice { Amount = sliceAmount, Eligible = eligible });
                }
                prev = lvl;
            }
            return pots;
        }

        // --- Helpers ---

        private int NextActiveIndex(int from)
        {
            // returns next index where the player has non-zero chip total OR is still in the hand
            for (int step = 1; step <= Players.Count; step++)
            {
                int idx = (from + step) % Players.Count;
                if (Players[idx].Chips + Players[idx].TotalCommitted > 0 && !Players[idx].HasFolded)
                    return idx;
            }
            return from;
        }
    }
}
