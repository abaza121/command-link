using System.Collections.Generic;

namespace CrossCut.CommandLink.Samples.TwoPeerArena
{
    public struct TwoPeerArenaTokenState
    {
        public byte PeerId;
        public uint SimNetId;
        public int CellX;
        public int CellY;
    }

    internal struct TwoPeerArenaMoveRecord
    {
        public byte PeerId;
        public uint SimNetId;
        public int TargetCellX;
        public int TargetCellY;
    }

    public sealed class TwoPeerArenaSimulation
    {
        public const int BoardWidth = 8;
        public const int BoardHeight = 5;

        private readonly Dictionary<uint, List<TwoPeerArenaMoveRecord>> _movesByTick = new Dictionary<uint, List<TwoPeerArenaMoveRecord>>();
        private readonly List<DecodedMoveCommand> _decodedMoves = new List<DecodedMoveCommand>(8);
        private readonly List<DecodedBuildPlaceCommand> _decodedBuilds = new List<DecodedBuildPlaceCommand>(2);
        private readonly List<DecodedRecruitCommand> _decodedRecruits = new List<DecodedRecruitCommand>(2);
        private readonly TwoPeerArenaTokenState[] _tokens = new TwoPeerArenaTokenState[2];

        public uint CurrentTick { get; private set; }
        public string LastSubmittedMoveSummary { get; private set; } = "none";
        public string LastResolvedMoveSummary { get; private set; } = "none";
        public string LastAppliedMoveSummary { get; private set; } = "none";
        public uint LastChecksum { get; private set; }

        public void Reset()
        {
            _movesByTick.Clear();
            _tokens[0] = new TwoPeerArenaTokenState
            {
                PeerId = 0,
                SimNetId = GetTokenSimNetId(0),
                CellX = 1,
                CellY = BoardHeight / 2,
            };
            _tokens[1] = new TwoPeerArenaTokenState
            {
                PeerId = 1,
                SimNetId = GetTokenSimNetId(1),
                CellX = BoardWidth - 2,
                CellY = BoardHeight / 2,
            };

            CurrentTick = 0;
            LastSubmittedMoveSummary = "none";
            LastResolvedMoveSummary = "none";
            LastAppliedMoveSummary = "none";
            LastChecksum = ComputeChecksum();
        }

        public void StageResolvedFrame(uint tick, in ResolvedInputFrame resolvedFrame)
        {
            DeterministicCommandPayload.DecodeResolvedFrame(resolvedFrame, _decodedMoves, _decodedBuilds, _decodedRecruits);

            if (_decodedMoves.Count == 0)
            {
                _movesByTick.Remove(tick);
                LastResolvedMoveSummary = $"tick {tick}: noop";
                return;
            }

            var stagedMoves = new List<TwoPeerArenaMoveRecord>(_decodedMoves.Count);
            for (int i = 0; i < _decodedMoves.Count; i++)
            {
                var move = _decodedMoves[i];
                stagedMoves.Add(new TwoPeerArenaMoveRecord
                {
                    PeerId = move.PeerId,
                    SimNetId = move.SimNetId,
                    TargetCellX = move.TargetCell.x,
                    TargetCellY = move.TargetCell.y,
                });
            }

            stagedMoves.Sort(CompareMoves);
            _movesByTick[tick] = stagedMoves;
            LastResolvedMoveSummary = FormatMoveSummary(tick, stagedMoves);
        }

        public void AdvanceTick(uint tick)
        {
            if (_movesByTick.TryGetValue(tick, out var stagedMoves))
            {
                stagedMoves.Sort(CompareMoves);

                for (int i = 0; i < stagedMoves.Count; i++)
                {
                    ApplyMove(stagedMoves[i]);
                }

                LastAppliedMoveSummary = FormatMoveSummary(tick, stagedMoves);
                _movesByTick.Remove(tick);
            }
            else
            {
                LastAppliedMoveSummary = $"tick {tick}: noop";
            }

            CurrentTick = tick + 1;
            LastChecksum = ComputeChecksum();
        }

        public void RecordSubmittedMove(byte peerId, int cellX, int cellY)
        {
            LastSubmittedMoveSummary = $"peer {peerId} -> ({cellX},{cellY})";
        }

        public bool TryGetTokenState(byte peerId, out TwoPeerArenaTokenState tokenState)
        {
            if (peerId < _tokens.Length)
            {
                tokenState = _tokens[peerId];
                return true;
            }

            tokenState = default;
            return false;
        }

        public uint ComputeChecksum()
        {
            unchecked
            {
                uint checksum = 2166136261u;
                checksum = Mix(checksum, CurrentTick);

                for (int i = 0; i < _tokens.Length; i++)
                {
                    checksum = Mix(checksum, _tokens[i].PeerId);
                    checksum = Mix(checksum, _tokens[i].SimNetId);
                    checksum = Mix(checksum, (uint)_tokens[i].CellX);
                    checksum = Mix(checksum, (uint)_tokens[i].CellY);
                }

                return checksum;
            }
        }

        public static uint GetTokenSimNetId(byte peerId)
        {
            return 1000u + peerId;
        }

        private static int CompareMoves(TwoPeerArenaMoveRecord left, TwoPeerArenaMoveRecord right)
        {
            int peerCompare = left.PeerId.CompareTo(right.PeerId);
            return peerCompare != 0 ? peerCompare : left.SimNetId.CompareTo(right.SimNetId);
        }

        private void ApplyMove(TwoPeerArenaMoveRecord move)
        {
            int tokenIndex = ResolveTokenIndex(move);
            if (tokenIndex < 0)
            {
                return;
            }

            var token = _tokens[tokenIndex];
            token.CellX = ClampToBoard(move.TargetCellX, BoardWidth);
            token.CellY = ClampToBoard(move.TargetCellY, BoardHeight);
            _tokens[tokenIndex] = token;
        }

        private int ResolveTokenIndex(TwoPeerArenaMoveRecord move)
        {
            for (int i = 0; i < _tokens.Length; i++)
            {
                if (_tokens[i].SimNetId == move.SimNetId)
                {
                    return i;
                }
            }

            for (int i = 0; i < _tokens.Length; i++)
            {
                if (_tokens[i].PeerId == move.PeerId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string FormatMoveSummary(uint tick, List<TwoPeerArenaMoveRecord> moves)
        {
            if (moves == null || moves.Count == 0)
            {
                return $"tick {tick}: noop";
            }

            var segments = new string[moves.Count];
            for (int i = 0; i < moves.Count; i++)
            {
                var move = moves[i];
                segments[i] = $"p{move.PeerId}->{move.TargetCellX},{move.TargetCellY}";
            }

            return $"tick {tick}: {string.Join(" | ", segments)}";
        }

        private static int ClampToBoard(int value, int dimension)
        {
            if (value < 0)
            {
                return 0;
            }

            if (value >= dimension)
            {
                return dimension - 1;
            }

            return value;
        }

        private static uint Mix(uint current, byte value)
        {
            return Mix(current, (uint)value);
        }

        private static uint Mix(uint current, uint value)
        {
            unchecked
            {
                current ^= value;
                current *= 16777619u;
                return current;
            }
        }
    }
}
