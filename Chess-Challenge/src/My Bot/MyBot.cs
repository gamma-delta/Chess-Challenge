using ChessChallenge.API;
using System.Linq;
using System.Collections.Generic;
using System;
using static System.Math;

// Basically just sunfish. Conveniently, Sebastian has already written the move *legality*
// code for us.
//
// Thank you to:
// - https://github.com/thomasahle/sunfish/blob/master/sunfish.py
// - https://zserge.com/posts/carnatus/
public class MyBot : IChessBot {
  // Sorry.
  static ulong[] PackedPST = {
    // P
    0x7f7f7f7f7f7f7f7f,
    0xcdd2d5c8e5d1d4d9,
    0x869c94aba79eab86,
    0x6e8f7d8e8d7f8e72,
    0x6582898885807f68,
    0x69888474757d826c,
    0x6087785a5b718260,
    0x7f7f7f7f7f7f7f7f,
    // N
    0x3d4a343475484539,
    0x7c79e35b83bd7b71,
    0x89c280c9c89abd7d,
    0x9797aca4a0a89890,
    0x7e849e9495a2817f,
    0x6d898c95918e8a71,
    0x6870817f817f686b,
    0x356865676c5c693a,
    // B
    0x44312d3368145a4d,
    0x7493a255589e8169,
    0x76a65fa8b3759b71,
    0x989093a199988e89,
    0x8c899096908f7f86,
    0x8d98978e8798938e,
    0x92938a858685938f,
    0x7881707371707575,
    // R
    0xa29ca083a4a0b7b1,
    0xb69cb7c2b6bda1bb,
    0x92a29ba0ac9a988e,
    0x7f848f8c917b7679,
    0x635c6f6a72625161,
    0x55635566665c6551,
    0x4a5960656254534a,
    0x61676d847d6d605f,
    // Q
    0x85807717c497d799,
    0x8d9fbb7593cbb897,
    0x7daa9fbbc7beaa81,
    0x806f959098937279,
    0x71707d7a7e756b69,
    0x617972746f746f64,
    0x5b6d7f6c70706a59,
    0x58616072605b5d55,
    // K
    0x83b5ae1c1cbbd241,
    0x5f89b6b7b7b68982,
    0x418b46ab3c9ba460,
    0x48b18a7b6c8c7f4e,
    0x48544b634c50774d,
    0x505554303f5f625f,
    0x7b82714d466d8c83,
    0x909d7c71857ea791,
  };

  // Unpack the enormous table down below into bytes.
  // I stored them as their values + 127 so as to not deal with signing, so get their values back:
  static List<int> PST = PackedPST.SelectMany(BitConverter.GetBytes).Select(b => b - 127).ToList(),
                   PieceValues = new() { 0, 100, 280, 320, 479, 929, 60000 };

  static int infinity = 999999999,
    maxDepth = 2;
  
  static Action<String> Print = Console.Out.WriteLine;
  // Action<String> Print = (_) => {};
  static void PrintIndent(int indent, string msg) {
    Print(new String(' ', indent) + msg);
  }

  
  public Move Think(Board board, Timer timer) {
    Move decision = Move.NullMove;
    int bestScore = -infinity;
    foreach (var move in board.GetLegalMoves()) {
      int score = negaMax(board, move, 0, 0);
      if (score > bestScore) {
        bestScore = score;
        decision = move;
      }
    }
    return decision;
  }

  static Dictionary<ulong, int> KnownScores = new();

  // B/c my point algorithm returns *deltas*, not points, we need to keep a running accumulator
  // of the overall change
  int negaMax(Board board, Move move, int depth, int overallDelta) {
    board.MakeMove(move);
    ulong projectedZobrist = board.ZobristKey;
    board.UndoMove(move);
  
    if (KnownScores.TryGetValue(projectedZobrist, out int known))
      return known;
    
    int inherentScore = deltaPoints(move, board);
    if (depth >= maxDepth)
      return overallDelta + inherentScore;

    // Check out what the opponent might do in response.
    // We want to minimize their best response.
    int theirBestGains = -infinity;
    PrintIndent(depth, $"Determining score for {move.GetSAN(board)}, ply {board.PlyCount}, acc {overallDelta}");
    PrintIndent(depth, $"Off the top of my head that's {inherentScore}");
    board.MakeMove(move);
    foreach (var theirResponse in board.GetLegalMoves()) {
      PrintIndent(depth, $"> Checking their best score if they did {theirResponse.GetSAN(board)} ...");
      // Because this is called recursively, we can assume by induction that
      // this is their best possible case.
      int theirScoreIfTheyDidThat = negaMax(board, theirResponse, depth + 1, overallDelta);
      PrintIndent(depth, $"> The best they could do is {theirScoreIfTheyDidThat}");
      if (theirScoreIfTheyDidThat > theirBestGains) {
        // Sweet
        PrintIndent(depth, $"> That's better than their previous best {theirBestGains} so they'll probably do it");
        theirBestGains = theirScoreIfTheyDidThat;
      }
    }

    board.UndoMove(move);
    // and my score is the inverse of theirs.
    int outScore = inherentScore + overallDelta - theirBestGains;
    PrintIndent(depth, $"From this they can get {theirBestGains} max so my score is {outScore}.");

    KnownScores[projectedZobrist] = outScore;
    return outScore;
  }

  // Evaluate how good this would be for the current player.
  static int deltaPoints(Move move, Board board) {
    bool checkmate;
    board.MakeMove(move); checkmate = board.IsInCheckmate(); board.UndoMove(move);
    if (checkmate) {
      return infinity;
    }
  
    // Sunfish has the white pieces in uppercase.
    int start = move.StartSquare.Index, end = move.TargetSquare.Index;
    PieceType me = move.MovePieceType;

    // The LUT always has *you* at the bottom, but Seb's has *white* at the bottom.
    // So we need to do some reversing maybe.
    int pstLookup(PieceType ty, int square) => PST[(int)(ty-1) * 64 +
      (board.IsWhiteToMove
        ? 63 - square
        : square)];

    // How many points we lose by leaving
    int score = -pstLookup(move.MovePieceType, start);
    // How many points we score by capturing that
    score += PieceValues[(int)move.CapturePieceType];

    // If we're not promoting, how many points we score at the target
    if (move.PromotionPieceType == PieceType.None)
      score += pstLookup(move.MovePieceType, end);
    else // Otherwise, how many points we score as a *queen*
      score += pstLookup(move.PromotionPieceType, end);

    // Account for points scored by moving the rook
    bool kingside = end > start;
    if (move.IsCastles)
      score += 
        -pstLookup(PieceType.Rook, start + (kingside ? 3 : -4)) // Score lost by leaving here
        + pstLookup(PieceType.Rook, end + (kingside ? -1 : 1)) // and gained by going there.
        + 200; // it's like card advantage, doing More Things is always better

    return score;
  }
}
