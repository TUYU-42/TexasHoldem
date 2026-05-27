using System;
using System.Collections.Generic;
using System.Linq;

namespace TexasHoldem
{
    /// <summary>
    /// Decision struct describing what an AI chose to do.
    /// </summary>
    public struct AiDecision
    {
        public PlayerAction Action;
        public int Amount; // total bet to match (for Raise)
    }

    /// <summary>
    /// Simple but coherent Texas Hold'em AI.
    /// - Pre-flop: Chen-style hand strength tier mapped to action.
    /// - Post-flop: rough hand strength (current rank + draw bonuses) → action.
    /// - Each AI has a personality (tight / loose / aggressive) sampled from its seat.
    /// </summary>
    public static class AiBrain
    {
        public enum Personality { Tight, Balanced, LooseAggressive }

        public static Personality PersonalityFor(int seat)
        {
            switch (seat % 3)
            {
                case 0: return Personality.Tight;
                case 1: return Personality.Balanced;
                default: return Personality.LooseAggressive;
            }
        }

        /// <summary>
        /// Main decision entry point.
        /// </summary>
        /// <param name="player">The AI player.</param>
        /// <param name="community">Current community cards (0,3,4 or 5).</param>
        /// <param name="callAmount">Chips needed to call this street.</param>
        /// <param name="minRaise">Minimum legal raise increment.</param>
        /// <param name="pot">Current pot (incl. all current bets).</param>
        /// <param name="rng">Random for variation.</param>
        public static AiDecision Decide(Player player, List<Card> community, int callAmount, int minRaise, int pot, Random rng)
        {
            Personality p = PersonalityFor(player.Seat);
            double strength = EstimateStrength(player.Hole, community);

            // Personality tweaks
            double aggression;
            double bluffRate;
            switch (p)
            {
                case Personality.Tight:
                    aggression = 0.7; bluffRate = 0.05;
                    strength -= 0.05;
                    break;
                case Personality.LooseAggressive:
                    aggression = 1.25; bluffRate = 0.18;
                    strength += 0.05;
                    break;
                default:
                    aggression = 1.0; bluffRate = 0.10;
                    break;
            }

            // Random noise to make play unpredictable
            strength += (rng.NextDouble() - 0.5) * 0.10;
            strength = Math.Max(0.0, Math.Min(1.0, strength));

            // Occasional bluff
            bool bluff = rng.NextDouble() < bluffRate;

            // No bet to call → check or bet
            if (callAmount == 0)
            {
                if (strength > 0.65 || bluff)
                {
                    int bet = ComputeRaise(player, pot, minRaise, strength, aggression, rng);
                    if (bet >= player.Chips) return new AiDecision { Action = PlayerAction.AllIn, Amount = player.Chips };
                    return new AiDecision { Action = PlayerAction.Raise, Amount = bet };
                }
                return new AiDecision { Action = PlayerAction.Check, Amount = 0 };
            }

            // There IS a bet to call
            double potOdds = (double)callAmount / Math.Max(1, pot + callAmount);

            // Strong hand: raise
            if (strength > 0.75)
            {
                int bet = ComputeRaise(player, pot, Math.Max(minRaise, callAmount + minRaise), strength, aggression, rng);
                if (bet >= player.Chips + player.CurrentBet)
                    return new AiDecision { Action = PlayerAction.AllIn, Amount = player.Chips + player.CurrentBet };
                return new AiDecision { Action = PlayerAction.Raise, Amount = bet };
            }

            // Decent hand or favorable pot odds: call
            if (strength > 0.45 || strength > potOdds * 1.2)
            {
                if (callAmount >= player.Chips)
                    return new AiDecision { Action = PlayerAction.AllIn, Amount = player.Chips + player.CurrentBet };
                return new AiDecision { Action = PlayerAction.Call, Amount = callAmount };
            }

            // Bluff: occasionally raise even when weak
            if (bluff && callAmount < player.Chips / 4)
            {
                int bet = ComputeRaise(player, pot, Math.Max(minRaise, callAmount + minRaise), 0.6, aggression, rng);
                return new AiDecision { Action = PlayerAction.Raise, Amount = bet };
            }

            // Cheap call with marginal hand
            if (callAmount <= pot / 10 && strength > 0.3)
            {
                return new AiDecision { Action = PlayerAction.Call, Amount = callAmount };
            }

            return new AiDecision { Action = PlayerAction.Fold, Amount = 0 };
        }

        private static int ComputeRaise(Player player, int pot, int minRaise, double strength, double aggression, Random rng)
        {
            // Bet sizing: fraction of pot scaled by strength & aggression
            double potFraction = (0.4 + strength * 0.7) * aggression + (rng.NextDouble() * 0.2 - 0.1);
            potFraction = Math.Max(0.3, Math.Min(1.5, potFraction));
            int desired = (int)(pot * potFraction);
            int target = Math.Max(minRaise, desired);
            target = Math.Max(target, player.CurrentBet + minRaise);
            target = Math.Min(target, player.Chips + player.CurrentBet);
            return target;
        }

        /// <summary>
        /// Returns 0..1 estimate of hand strength.
        /// Pre-flop: hand category tiers.
        /// Post-flop: actual hand rank (current 5 best) + draw bonuses.
        /// </summary>
        private static double EstimateStrength(List<Card> hole, List<Card> community)
        {
            if (community == null || community.Count == 0)
                return PreFlopStrength(hole);

            var all = hole.Concat(community).ToList();
            var hv = HandEvaluator.Evaluate(all);

            double baseScore;
            switch (hv.Rank)
            {
                case HandRank.HighCard:      baseScore = 0.15; break;
                case HandRank.OnePair:       baseScore = 0.40; break;
                case HandRank.TwoPair:       baseScore = 0.62; break;
                case HandRank.ThreeOfAKind:  baseScore = 0.78; break;
                case HandRank.Straight:      baseScore = 0.85; break;
                case HandRank.Flush:         baseScore = 0.90; break;
                case HandRank.FullHouse:     baseScore = 0.94; break;
                case HandRank.FourOfAKind:   baseScore = 0.98; break;
                case HandRank.StraightFlush:
                case HandRank.RoyalFlush:    baseScore = 1.0;  break;
                default:                     baseScore = 0.2;  break;
            }

            // Pair quality boost (top pair vs bottom pair)
            if (hv.Rank == HandRank.OnePair && hv.TieBreakers.Length > 0)
            {
                int pairRank = hv.TieBreakers[0];
                baseScore += (pairRank - 7) * 0.012; // adjust for pair height
            }

            // Draw bonuses if not on river yet
            if (community.Count < 5)
            {
                if (HasFlushDraw(all)) baseScore += 0.10;
                if (HasOpenEndedStraightDraw(all)) baseScore += 0.08;
            }
            return Math.Max(0, Math.Min(1, baseScore));
        }

        private static double PreFlopStrength(List<Card> hole)
        {
            if (hole.Count < 2) return 0.3;
            int r1 = Math.Max(hole[0].Rank, hole[1].Rank);
            int r2 = Math.Min(hole[0].Rank, hole[1].Rank);
            bool pair = r1 == r2;
            bool suited = hole[0].Suit == hole[1].Suit;
            int gap = r1 - r2;

            if (pair)
            {
                if (r1 >= 13) return 0.95;        // AA, KK
                if (r1 >= 11) return 0.85;        // QQ, JJ
                if (r1 >= 9)  return 0.72;        // 99, TT
                if (r1 >= 6)  return 0.6;
                return 0.5;
            }
            if (r1 == 14)
            {
                if (r2 >= 13) return suited ? 0.9 : 0.85;   // AK
                if (r2 >= 11) return suited ? 0.78 : 0.7;   // AQ, AJ
                if (suited)   return 0.62;                  // any suited ace
                return 0.45;
            }
            if (r1 == 13 && r2 >= 11) return suited ? 0.7 : 0.62;
            if (r1 >= 11 && r2 >= 10) return suited ? 0.65 : 0.55;
            if (suited && gap <= 2 && r1 >= 9) return 0.55;
            if (gap <= 1 && r1 >= 8)  return 0.45;
            if (suited)               return 0.35;
            return 0.2;
        }

        private static bool HasFlushDraw(List<Card> cards)
        {
            return cards.GroupBy(c => c.Suit).Any(g => g.Count() == 4);
        }

        private static bool HasOpenEndedStraightDraw(List<Card> cards)
        {
            var ranks = cards.Select(c => c.Rank).Distinct().OrderBy(r => r).ToList();
            for (int i = 0; i <= ranks.Count - 4; i++)
            {
                if (ranks[i + 3] - ranks[i] == 3) return true;
            }
            return false;
        }
    }
}
