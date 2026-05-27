using System;

namespace TexasHoldem
{
    public enum Suit { Spades, Hearts, Diamonds, Clubs }

    /// <summary>
    /// Represents a single playing card. Rank is 2-14 where 11=J, 12=Q, 13=K, 14=A.
    /// </summary>
    public class Card
    {
        public int Rank { get; }
        public Suit Suit { get; }

        public Card(int rank, Suit suit)
        {
            if (rank < 2 || rank > 14)
                throw new ArgumentException("Rank must be 2..14");
            Rank = rank;
            Suit = suit;
        }

        public string RankSymbol
        {
            get
            {
                switch (Rank)
                {
                    case 11: return "J";
                    case 12: return "Q";
                    case 13: return "K";
                    case 14: return "A";
                    default: return Rank.ToString();
                }
            }
        }

        public string SuitLetter
        {
            get
            {
                switch (Suit)
                {
                    case Suit.Spades: return "S";
                    case Suit.Hearts: return "H";
                    case Suit.Diamonds: return "D";
                    case Suit.Clubs: return "C";
                    default: return "?";
                }
            }
        }

        /// <summary>
        /// Filename used to look up the card image, e.g. "AS" or "10H".
        /// </summary>
        public string ImageKey => RankSymbol + SuitLetter;

        public override string ToString() => ImageKey;
    }
}
