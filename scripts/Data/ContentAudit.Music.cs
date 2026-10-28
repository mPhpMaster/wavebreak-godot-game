using System.Collections.Generic;
using Godot;
using Wavebreak.Audio;
using Wavebreak.Meta;

namespace Wavebreak.Data;

/// <summary>
/// Music in the audit: every map has its own run track and a remix, the map's
/// MusicId is what the director actually plays there, every catalogued track
/// file loads (as OGG, with a sane length and the right loop flag), every
/// unlock it names exists, and no file in assets/music is missing from the
/// catalogue (so the Boombox lists everything).
/// </summary>
public static partial class ContentAudit
{
    private const string MusicDir = "res://assets/music/";

    private static void AuditMusic()
    {
        var ids = new HashSet<string>();
        int loops = 0, stings = 0;
        long bytes = 0;
        foreach (TrackDef t in MusicTracks.All)
        {
            Check(ids.Add(t.Id), $"music: duplicate track id {t.Id}");
            if (t.Loop) loops++; else stings++;

            AudioStream? stream = MusicDirector.Load(t.Id);
            Check(stream is AudioStreamOggVorbis, $"music: {t.Id}.ogg does not load as OGG");
            if (stream is AudioStreamOggVorbis ogg)
            {
                double len = ogg.GetLength();
                Check(ogg.Loop == t.Loop, $"music: {t.Id} loop flag {ogg.Loop}, want {t.Loop}");
                Check(t.Loop ? len >= 20.0 && len <= 120.0 : len > 0.5 && len <= 15.0,
                      $"music: {t.Id} is {len:0.0}s ({(t.Loop ? "loop wants 20-120" : "sting wants <15")})");
            }
            using (FileAccess? f = FileAccess.Open(MusicDir + t.Id + ".ogg", FileAccess.ModeFlags.Read))
            {
                if (f is not null)
                {
                    long size = (long)f.GetLength();
                    bytes += size;
                    Check(size is > 0 and <= 1_200_000, $"music: {t.Id}.ogg is {size / 1024} KB (keep under ~1 MB)");
                }
                else
                {
                    // Exports remap OGG files to imported resources; source size budgets apply before export.
                    Check(!OS.HasFeature("editor") && stream is AudioStreamOggVorbis,
                        $"music: {t.Id} has neither source audio nor a valid exported stream");
                }
            }

            Check(t.UnlockedBy.Length == 0 || Unlocks.ById(t.UnlockedBy) is not null,
                  $"music: {t.Id} unlocked by unknown quest '{t.UnlockedBy}'");
            Check(t.Free || t.OpensWhenHeard || t.UnlockedBy.Length > 0, $"music: {t.Id} can never be unlocked");
            if (t.Kind is TrackKind.Map or TrackKind.Remix)
                Check(Stages.ById(t.MapId) is not null, $"music: {t.Id} names unknown map '{t.MapId}'");
            Check(MusicTracks.HowToUnlock(t).Length > 0, $"music: {t.Id} has no unlock text");
        }

        // One run track and one remix per map, and the director agrees.
        foreach (StageDef s in Stages.All)
        {
            TrackDef? own = MusicTracks.ForMap(s.Id);
            Check(own is not null, $"music: map {s.Id} has no run track");
            if (own is null) continue;
            Check(s.MusicId.Length == 0 || s.MusicId == own.Id, $"music: map {s.Id} MusicId '{s.MusicId}' is not its track '{own.Id}'");
            Check(MusicDirector.TrackForStage(s.Id, 1) == own.Id,
                  $"music: director plays '{MusicDirector.TrackForStage(s.Id, 1)}' on {s.Id}, want '{own.Id}'");
            bool remix = false;
            foreach (TrackDef t in MusicTracks.All) remix |= t.Kind == TrackKind.Remix && t.MapId == s.Id;
            Check(remix, $"music: map {s.Id} has no remix");
        }

        // Distinct maps get distinct tracks.
        var mapTracks = new HashSet<string>();
        foreach (StageDef s in Stages.All)
        {
            string track = MusicDirector.TrackForStage(s.Id, 1);
            Check(mapTracks.Add(track), $"music: map {s.Id} shares track '{track}'");
        }

        foreach (string must in new[] { "title", "boss", "boss_final", "swarm", "shop", "victory", "fanfare", "defeat" })
            Check(MusicTracks.ById(must) is not null, $"music: no '{must}' track");

        // Every file on disk is in the catalogue.
        int files = 0;
        foreach (string file in DirAccess.GetFilesAt(MusicDir))
        {
            if (!file.EndsWith(".ogg")) continue;
            files++;
            Check(MusicTracks.ById(file[..^4]) is not null, $"music: {file} is not in MusicTracks");
        }

        // The Boombox's saved state is never read in an automated launch.
        Boombox.Load();
        Check(Boombox.Mode == BoomboxMode.MapDefault, $"music: automated launch picked up Boombox mode {Boombox.Mode}");

        Line($"music tracks={MusicTracks.All.Count} loops={loops} stings={stings} files={files} maps={Stages.All.Count} " +
             $"size={bytes / 1024} KB");
    }
}
