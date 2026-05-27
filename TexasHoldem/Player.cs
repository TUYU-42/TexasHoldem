using System.Collections.Generic;

namespace TexasHoldem
{
    public enum PlayerAction { None, Fold, Check, Call, Raise, AllIn }

    /// <summary>
    /// Represents a seated player (human or AI). Holds chips, hole cards, and current-street state.
    /// </summary>
    public class Player
    {
        public string Name { get; }
        public bool IsHuman { get; }
        public int Seat { get; }            // 0 = human, others = AI

        public int Chips { get; set; }
        public List<Card> Hole { get; } = new List<Card>();

        public int CurrentBet { get; set; }     // chips committed this betting round
        public int TotalCommitted { get; set; } // total chips put into the pot this hand
        public bool HasFolded { get; set; }
        public bool IsAllIn { get; set; }
        public bool HasActed { get; set; }      // acted on the current street
        public PlayerAction LastAction { get; set; }

        public Player(int seat, string name, bool isHuman, int chips)
        {
            Seat = seat;
            Name = name;
            IsHuman = isHuman;
            Chips = chips;
        }

        public bool IsInHand => !HasFolded && Chips + TotalCommitted > 0;
        public bool CanAct => !HasFolded && !IsAllIn && Chips > 0;

        /// <summary>Reset state for a new hand.</summary>
        public void NewHand()
        {
            Hole.Clear();
            CurrentBet = 0;
            TotalCommitted = 0;
            HasFolded = false;
            IsAllIn = false;
            HasActed = false;
            LastAction = PlayerAction.None;
        }

        /// <summary>Reset state for a new betting street.</summary>
        public void NewStreet()
        {
            CurrentBet = 0;
            HasActed = false;
            if (!HasFolded && !IsAllIn) LastAction = PlayerAction.None;
        }

        /// <summary>
        /// Moves chips from stack into the current bet. Returns the amount actually committed
        /// (may be less than requested if it would exceed remaining chips → all-in).
        /// </summary>
        public int Commit(int amount)
        {
            int actual = amount;
            if (actual >= Chips)
            {
                actual = Chips;
                IsAllIn = true;
            }
            Chips -= actual;
            CurrentBet += actual;
            TotalCommitted += actual;
            return actual;
        }
    }
}
