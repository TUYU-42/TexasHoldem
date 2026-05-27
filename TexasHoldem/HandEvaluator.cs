using System;
using System.Collections.Generic;
using System.Linq;

namespace TexasHoldem
{
    public enum HandRank
    {
        HighCard = 1,
        OnePair = 2,
        TwoPair = 3,
        ThreeOfAKind = 4,
        Straight = 5,
        Flush = 6,
        FullHouse = 7,
        FourOfAKind = 8,
        StraightFlush = 9,
        RoyalFlush = 10
    }

    /// <summary>
    /// Result of evaluating a 5-card hand. Comparable so we can sort hands.
    /// Score = (rank << 24) | tiebreaker; bigger is better.
    /// </summary>
    public class HandValue : IComparable<HandValue>
    {
        public HandRank Rank { get; }
        public int[] TieBreakers { get; } // up to 5 kicker ranks, high to low
        public string Description { get; }

        public HandValue(HandRank rank, int[] tieBreakers, string description)
        {
            Rank = rank;
            TieBreakers = tieBreakers;
            Description = description;
        }

        public int CompareTo(HandValue other)
        {
            if (other == null) return 1;
            int c = Rank.CompareTo(other.Rank);
            if (c != 0) return c;
            for (int i = 0; i < Math.Min(TieBreakers.Length, other.TieBreakers.Length); i++)
            {
                c = TieBreakers[i].CompareTo(other.TieBreakers[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        public override string ToString() => Description;
    }

    /// <summary>
    /// Finds the best 5-card poker hand from any number of cards (2..7).
    /// </summary>
    public static class HandEvaluator
    {
        public static HandValue Evaluate(IEnumerable<Card> cards)
        {
            List<Card> all = cards.ToList();
            if (all.Count < 5)
            {
                // Pad evaluation with what we have (used for AI strength estimates pre-flop)
                return EvaluateUpToFive(all);
            }

            HandValue best = null;
            // Choose 5 of N (N up to 7) => at most C(7,5)=21 combos
            var combos = Combinations(all, 5);
            foreach (var combo in combos)
            {
                HandValue hv = EvaluateFive(combo);
                if (best == null || hv.CompareTo(best) > 0)
                    best = hv;
            }
            return best;
        }

        private static HandValue EvaluateUpToFive(List<Card> cards)
        {
            // Rough estimate when fewer than 5 cards (used for pre-flop strength only)
            var ranks = cards.Select(c => c.Rank).OrderByDescending(r => r).ToArray();
            return new HandValue(HandRank.HighCard, ranks, "Partial");
        }

        private static HandValue EvaluateFive(List<Card> cards)
        {
            var ranks = cards.Select(c => c.Rank).OrderByDescending(r => r).ToList();
            var suits = cards.Select(c => c.Suit).ToList();

            bool flush = suits.Distinct().Count() == 1;
            var straightHigh = StraightHigh(ranks);
            bool straight = straightHigh > 0;

            // Group by rank: count -> ranks of that count
            var groups = ranks.GroupBy(r => r)
                              .Select(g => new { Rank = g.Key, Count = g.Count() })
                              .OrderByDescending(x => x.Count)
                              .ThenByDescending(x => x.Rank)
                              .ToList();

            int[] counts = groups.Select(g => g.Count).ToArray();
            int[] groupRanks = groups.Select(g => g.Rank).ToArray();

            if (flush && straight && straightHigh == 14)
                return new HandValue(HandRank.RoyalFlush, new[] { 14 }, "Royal Flush");
            if (flush && straight)
                return new HandValue(HandRank.StraightFlush, new[] { straightHigh }, $"Straight Flush, {RankName(straightHigh)} high");
            if (counts[0] == 4)
                return new HandValue(HandRank.FourOfAKind, new[] { groupRanks[0], groupRanks[1] }, $"Four of a Kind, {RankName(groupRanks[0])}s");
            if (counts[0] == 3 && counts.Length > 1 && counts[1] >= 2)
                return new HandValue(HandRank.FullHouse, new[] { groupRanks[0], groupRanks[1] }, $"Full House, {RankName(groupRanks[0])}s over {RankName(groupRanks[1])}s");
            if (flush)
                return new HandValue(HandRank.Flush, ranks.ToArray(), $"Flush, {RankName(ranks[0])} high");
            if (straight)
                return new HandValue(HandRank.Straight, new[] { straightHigh }, $"Straight, {RankName(straightHigh)} high");
            if (counts[0] == 3)
                return new HandValue(HandRank.ThreeOfAKind, new[] { groupRanks[0], groupRanks[1], groupRanks[2] }, $"Three of a Kind, {RankName(groupRanks[0])}s");
            if (counts[0] == 2 && counts.Length > 1 && counts[1] == 2)
                return new HandValue(HandRank.TwoPair, new[] { groupRanks[0], groupRanks[1], groupRanks[2] }, $"Two Pair, {RankName(groupRanks[0])}s and {RankName(groupRanks[1])}s");
            if (counts[0] == 2)
                return new HandValue(HandRank.OnePair, new[] { groupRanks[0], groupRanks[1], groupRanks[2], groupRanks[3] }, $"Pair of {RankName(groupRanks[0])}s");
            return new HandValue(HandRank.HighCard, ranks.ToArray(), $"High Card {RankName(ranks[0])}");
        }

        /// <summary>
        /// Returns the high card of a straight if present, else 0.
        /// Handles the wheel A-2-3-4-5 (returns 5).
        /// </summary>
        private static int StraightHigh(List<int> ranks)
        {
            var unique = ranks.Distinct().OrderByDescending(r => r).ToList();
            if (unique.Count < 5) return 0;
            // Standard straights
            for (int i = 0; i <= unique.Count - 5; i++)
            {
                if (unique[i] - unique[i + 4] == 4)
                    return unique[i];
            }
            // Wheel: A,5,4,3,2
            if (unique.Contains(14) && unique.Contains(5) && unique.Contains(4) && unique.Contains(3) && unique.Contains(2))
                return 5;
            return 0;
        }

        private static string RankName(int r)
        {
            switch (r)
            {
                case 14: return "Ace";
                case 13: return "King";
                case 12: return "Queen";
                case 11: return "Jack";
                default: return r.ToString();
            }
        }

        private static IEnumerable<List<T>> Combinations<T>(List<T> source, int k)
        {
            int n = source.Count;
            if (k > n) yield break;
            int[] idx = new int[k];
            for (int i = 0; i < k; i++) idx[i] = i;
            while (true)
            {
                var combo = new List<T>(k);
                for (int i = 0; i < k; i++) combo.Add(source[idx[i]]);
                yield return combo;
                int t = k - 1;
                while (t >= 0 && idx[t] == n - k + t) t--;
                if (t < 0) yield break;
                idx[t]++;
                for (int i = t + 1; i < k; i++) idx[i] = idx[i - 1] + 1;
            }
        }
    }
}
