using System;
using System.Collections.Generic;

namespace TexasHoldem
{
    /// <summary>
    /// Standard 52-card deck, Fisher-Yates shuffled.
    /// </summary>
    public class Deck
    {
        private readonly List<Card> _cards = new List<Card>();
        private readonly Random _rng;

        public Deck(Random rng = null)
        {
            _rng = rng ?? new Random();
            Reset();
        }

        public int Count => _cards.Count;

        public void Reset()
        {
            _cards.Clear();
            for (int s = 0; s < 4; s++)
                for (int r = 2; r <= 14; r++)
                    _cards.Add(new Card(r, (Suit)s));
            Shuffle();
        }

        public void Shuffle()
        {
            // Fisher-Yates
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                Card tmp = _cards[i];
                _cards[i] = _cards[j];
                _cards[j] = tmp;
            }
        }

        public Card Draw()
        {
            if (_cards.Count == 0)
                throw new InvalidOperationException("Deck is empty");
            Card c = _cards[_cards.Count - 1];
            _cards.RemoveAt(_cards.Count - 1);
            return c;
        }
    }
}
