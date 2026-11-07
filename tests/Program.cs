using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrucoRDR2;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("Deck has 40 unique cards", DeckHasFortyUniqueCards);
        Run("Deck contains no eights or nines", DeckHasNoEightOrNine);
        Run("Card hierarchy follows Argentine truco", CardHierarchy);
        Run("Envido evaluates same-suit pair", EnvidoPair);
        Run("Envido figures count as zero", EnvidoFigures);
        Run("Flor requires three cards of one suit", FlorRules);
        Run("Envido ties go to mano", EnvidoTieGoesToMano);
        Run("Rejected envido awards one point to caller", RejectedEnvido);
        Run("Accepted envido awards two points to winner", AcceptedEnvido);
        Run("Rejected truco awards one point to caller", RejectedTruco);
        Run("Wanted truco raises stake to two", AcceptedTruco);
        Run("Out-of-turn card play is rejected", RejectsOutOfTurnPlay);
        Run("Going to the deck before first card concedes two", FoldBeforeFirstCard);
        Run("Match alternates mano after a completed hand", MatchAlternatesMano);
        Run("AI raises its calls based on hand value", AiCallPolicy);
        Run("AI chooses measured envido raises", AiEnvidoRaisePolicy);
        Run("AI changes risk thresholds with score", AiScorePolicy);
        Run("Settings writer preserves unrelated INI keys", SettingsPersistence);
        Run("Match checkpoint round-trips", MatchCheckpointRoundTrip);
        Run("Corrupt checkpoint is ignored", CorruptCheckpointIsIgnored);

        Console.WriteLine();
        Console.WriteLine("Resultados: " + _passed + " correctas; " + _failed + " fallidas.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("[OK] " + name);
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine("[FALLO] " + name + " — " + ex.Message);
        }
    }

    private static void DeckHasFortyUniqueCards()
    {
        List<TrucoCard> deck = TrucoDeck.CreateShuffled(new Random(17));
        Equal(40, deck.Count);
        Equal(40, deck.Select(c => c.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static void DeckHasNoEightOrNine()
    {
        foreach (TrucoCard card in TrucoDeck.CreateShuffled(new Random(21)))
            True(card.Rank != 8 && card.Rank != 9, "El mazo contiene 8 o 9.");
    }

    private static void CardHierarchy()
    {
        True(Power(1, SpanishSuit.Espadas) > Power(1, SpanishSuit.Bastos), "Ancho de espada debe superar al de basto.");
        True(Power(1, SpanishSuit.Bastos) > Power(7, SpanishSuit.Espadas), "Ancho de basto debe superar al siete de espada.");
        True(Power(7, SpanishSuit.Espadas) > Power(7, SpanishSuit.Oros), "Siete de espada debe superar al siete de oro.");
        True(Power(7, SpanishSuit.Oros) > Power(3, SpanishSuit.Copas), "Siete de oro debe superar a los tres.");
        True(Power(3, SpanishSuit.Oros) > Power(2, SpanishSuit.Oros), "Los tres deben superar a los dos.");
        True(Power(2, SpanishSuit.Oros) > Power(1, SpanishSuit.Copas), "Los dos deben superar a los ases comunes.");
        True(Power(1, SpanishSuit.Copas) > Power(12, SpanishSuit.Oros), "Los ases comunes deben superar a las figuras.");
        True(Power(12, SpanishSuit.Oros) > Power(11, SpanishSuit.Oros), "Doce debe superar a once.");
        True(Power(11, SpanishSuit.Oros) > Power(10, SpanishSuit.Oros), "Once debe superar a diez.");
        True(Power(10, SpanishSuit.Oros) > Power(7, SpanishSuit.Copas), "Diez debe superar a siete común.");
        True(Power(7, SpanishSuit.Copas) > Power(6, SpanishSuit.Oros), "Siete común debe superar a seis.");
        True(Power(6, SpanishSuit.Oros) > Power(5, SpanishSuit.Oros), "Seis debe superar a cinco.");
        True(Power(5, SpanishSuit.Oros) > Power(4, SpanishSuit.Oros), "Cinco debe superar a cuatro.");
    }

    private static int Power(int rank, SpanishSuit suit)
    {
        return TrucoRules.TrucoPower(new TrucoCard(rank, suit));
    }

    private static void EnvidoPair()
    {
        List<TrucoCard> hand = new List<TrucoCard>
        {
            new TrucoCard(6, SpanishSuit.Oros),
            new TrucoCard(5, SpanishSuit.Oros),
            new TrucoCard(12, SpanishSuit.Copas)
        };
        Equal(31, TrucoRules.EnvidoPoints(hand));
    }

    private static void EnvidoFigures()
    {
        List<TrucoCard> hand = new List<TrucoCard>
        {
            new TrucoCard(12, SpanishSuit.Copas),
            new TrucoCard(11, SpanishSuit.Copas),
            new TrucoCard(7, SpanishSuit.Oros)
        };
        Equal(20, TrucoRules.EnvidoPoints(hand));
    }

    private static void FlorRules()
    {
        List<TrucoCard> flor = new List<TrucoCard>
        {
            new TrucoCard(7, SpanishSuit.Bastos),
            new TrucoCard(6, SpanishSuit.Bastos),
            new TrucoCard(12, SpanishSuit.Bastos)
        };
        List<TrucoCard> noFlor = new List<TrucoCard>
        {
            new TrucoCard(7, SpanishSuit.Bastos),
            new TrucoCard(6, SpanishSuit.Bastos),
            new TrucoCard(12, SpanishSuit.Oros)
        };
        True(TrucoRules.HasFlor(flor), "La mano debería tener flor.");
        Equal(33, TrucoRules.FlorPoints(flor));
        True(!TrucoRules.HasFlor(noFlor), "La mano no debería tener flor.");
        Equal(0, TrucoRules.FlorPoints(noFlor));
    }

    private static void EnvidoTieGoesToMano()
    {
        List<TrucoCard> first = new List<TrucoCard>
        {
            new TrucoCard(7, SpanishSuit.Oros),
            new TrucoCard(6, SpanishSuit.Copas),
            new TrucoCard(5, SpanishSuit.Bastos)
        };
        List<TrucoCard> second = new List<TrucoCard>
        {
            new TrucoCard(7, SpanishSuit.Espadas),
            new TrucoCard(6, SpanishSuit.Oros),
            new TrucoCard(5, SpanishSuit.Copas)
        };
        Equal(7, TrucoRules.EnvidoPoints(first));
        Equal(7, TrucoRules.EnvidoPoints(second));
        Equal(TrucoPlayer.Opponent, TrucoRules.CompareEnvido(first, second, TrucoPlayer.Opponent));
    }

    private static void RejectedEnvido()
    {
        TrucoRound round = CreateRound(TrucoPlayer.Player, 12);
        TrucoPlayer caller = round.CurrentPlayer;
        TrucoPlayer responder = Other(caller);
        int awarded = 0;
        TrucoPlayer winner = Other(caller);
        round.PointsAwarded += (player, points, reason) => { winner = player; awarded += points; };

        True(round.CallEnvido(caller, TrucoCall.Envido), "No se pudo cantar Envido.");
        True(round.RespondPending(responder, false), "No se pudo rechazar Envido.");
        Equal(caller, winner);
        Equal(1, awarded);
        True(round.EnvidoResolved, "El envido debería quedar cerrado.");
    }

    private static void AcceptedEnvido()
    {
        TrucoRound round = CreateRound(TrucoPlayer.Opponent, 13);
        TrucoPlayer caller = round.CurrentPlayer;
        int awarded = 0;
        TrucoPlayer winner = Other(caller);
        round.PointsAwarded += (player, points, reason) => { winner = player; awarded += points; };

        True(round.CallEnvido(caller, TrucoCall.Envido), "No se pudo cantar Envido.");
        True(round.RespondPending(Other(caller), true), "No se pudo querer Envido.");
        Equal(TrucoRules.CompareEnvido(round.PlayerCards, round.OpponentCards, round.Mano), winner);
        Equal(2, awarded);
    }

    private static void RejectedTruco()
    {
        TrucoRound round = CreateRound(TrucoPlayer.Player, 14);
        TrucoPlayer caller = round.CurrentPlayer;
        int awarded = 0;
        TrucoPlayer winner = Other(caller);
        round.PointsAwarded += (player, points, reason) => { winner = player; awarded += points; };

        True(round.CallTruco(caller), "No se pudo cantar Truco.");
        True(round.RespondPending(Other(caller), false), "No se pudo rechazar Truco.");
        Equal(caller, winner);
        Equal(1, awarded);
        True(round.IsComplete, "La mano debería terminar.");
    }

    private static void AcceptedTruco()
    {
        TrucoRound round = CreateRound(TrucoPlayer.Player, 15);
        TrucoPlayer caller = round.CurrentPlayer;
        True(round.CallTruco(caller), "No se pudo cantar Truco.");
        True(round.RespondPending(Other(caller), true), "No se pudo querer Truco.");
        Equal(2, round.TrucoStake);
        True(!round.HasPendingResponse, "No debería quedar una respuesta pendiente.");
    }

    private static void RejectsOutOfTurnPlay()
    {
        TrucoRound round = CreateRound(TrucoPlayer.Player, 16);
        TrucoPlayer first = round.CurrentPlayer;
        round.PlayCard(first, 0);
        Equal(Other(first), round.CurrentPlayer);
        Throws<InvalidOperationException>(() => round.PlayCard(first, 0));
    }

    private static void FoldBeforeFirstCard()
    {
        TrucoRound round = CreateRound(TrucoPlayer.Player, 18);
        TrucoPlayer foldingPlayer = round.CurrentPlayer;
        int awarded = 0;
        TrucoPlayer winner = foldingPlayer;
        round.PointsAwarded += (player, points, reason) => { winner = player; awarded = points; };
        True(round.Fold(foldingPlayer), "No se pudo ir al mazo.");
        Equal(Other(foldingPlayer), winner);
        Equal(2, awarded);
    }

    private static void MatchAlternatesMano()
    {
        TrucoMatch match = new TrucoMatch(15, false, new Random(29));
        TrucoPlayer firstMano = match.Mano;
        True(match.Round.Fold(match.Round.CurrentPlayer), "No se pudo cerrar la mano de prueba.");
        match.StartNextRound();
        Equal(Other(firstMano), match.Mano);
    }

    private static void AiCallPolicy()
    {
        True(!TrucoAi.ChooseEnvidoCall(28, 0, 0, 30).HasValue, "No debería cantar con 28.");
        Equal(TrucoCall.Envido, TrucoAi.ChooseEnvidoCall(29, 0, 0, 30).Value);
        Equal(TrucoCall.RealEnvido, TrucoAi.ChooseEnvidoCall(32, 0, 0, 30).Value);
        Equal(TrucoCall.FaltaEnvido, TrucoAi.ChooseEnvidoCall(32, 25, 20, 30).Value);
    }

    private static void AiEnvidoRaisePolicy()
    {
        Equal(TrucoCall.RealEnvido, TrucoAi.ChooseEnvidoRaise(31, TrucoCall.Envido, 0, 0, 30).Value);
        Equal(TrucoCall.FaltaEnvido, TrucoAi.ChooseEnvidoRaise(32, TrucoCall.Envido, 26, 20, 30).Value);
        Equal(TrucoCall.Envido, TrucoAi.ChooseEnvidoRaise(29, TrucoCall.Envido, 5, 12, 30).Value);
        True(!TrucoAi.ChooseEnvidoRaise(25, TrucoCall.Envido, 0, 0, 30).HasValue,
            "No debería subir con un envido bajo.");
    }

    private static void AiScorePolicy()
    {
        True(TrucoAi.ShouldCallTruco(26, 0, 10, 30), "El rival atrasado debería aceptar algo más de riesgo.");
        True(!TrucoAi.ShouldCallTruco(26, 10, 0, 30), "El rival que lidera debería exigir más fuerza.");
        True(TrucoAi.ShouldFold(8, 2, 28, 20, 30), "Con ventaja y mano débil puede conceder.");
        True(!TrucoAi.ShouldFold(8, 2, 20, 28, 30), "No debería conceder estando abajo.");
        True(TrucoAi.ShouldAcceptTruco(22, TrucoCall.Truco, 0, 10, 30), "Debe asumir más riesgo si está perdiendo.");
        True(!TrucoAi.ShouldAcceptTruco(22, TrucoCall.Truco, 10, 0, 30), "Debe proteger una ventaja.");
    }

    private static void SettingsPersistence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TrucoRDR2-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "TrucoRDR2.ini");
        try
        {
            File.WriteAllLines(path, new[] { "[Textures]", "TextureDictionary=cards", "[Game]", "TargetPoints=30", "FlorEnabled=false", "[Audio]", "Enabled=true", "[Custom]", "Keep=1" });
            TrucoConfiguration.SaveGameplaySettings(path, 15, true, false);
            string saved = File.ReadAllText(path);
            True(saved.Contains("TextureDictionary=cards"), "Se perdió la configuración de texturas.");
            True(saved.Contains("TargetPoints=15"), "No se guardó el tanteador.");
            True(saved.Contains("FlorEnabled=true"), "No se guardó Flor.");
            True(saved.Contains("Enabled=false"), "No se guardó el audio.");
            True(saved.Contains("Keep=1"), "Se perdió una sección ajena.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void MatchCheckpointRoundTrip()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TrucoRDR2-save-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "TrucoRDR2.save");
        try
        {
            TrucoMatchProgress original = new TrucoMatchProgress(30, 12, 8,
                TrucoPlayer.Opponent, true, false, null);
            TrucoMatchProgressStore.Save(path, original);
            TrucoMatchProgress loaded;
            True(TrucoMatchProgressStore.TryLoad(path, out loaded), "No se pudo leer el punto de guardado.");
            Equal(30, loaded.TargetPoints);
            Equal(12, loaded.PlayerScore);
            Equal(8, loaded.OpponentScore);
            Equal(TrucoPlayer.Opponent, loaded.Mano);
            True(loaded.FlorEnabled, "No se restauró Flor.");
            True(!loaded.IsComplete, "El guardado no debería estar terminado.");
            True(!loaded.Winner.HasValue, "No debería haber ganador todavía.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void CorruptCheckpointIsIgnored()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TrucoRDR2-save-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "TrucoRDR2.save");
        try
        {
            File.WriteAllText(path, "Version=bad\nTargetPoints=999");
            TrucoMatchProgress loaded;
            True(!TrucoMatchProgressStore.TryLoad(path, out loaded), "Se aceptó un guardado corrupto.");
            True(loaded == null, "El objeto de guardado debería quedar vacío.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static TrucoRound CreateRound(TrucoPlayer mano, int seed)
    {
        TrucoRound round = new TrucoRound(mano, new Random(seed), 30, 0, 0, false);
        round.Initialize();
        return round;
    }

    private static TrucoPlayer Other(TrucoPlayer player)
    {
        return player == TrucoPlayer.Player ? TrucoPlayer.Opponent : TrucoPlayer.Player;
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException("Esperado: " + expected + "; obtenido: " + actual + ".");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException("No se lanzó " + typeof(TException).Name + ".");
    }
}