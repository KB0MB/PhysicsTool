using System;
using System.Windows.Forms;

namespace HKCLTool;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && string.Equals(args[0], "--inspect-conversion-matches", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(ConversionMatchAnalyzer.Format(ConversionMatchAnalyzer.Analyze(args[1])));
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphysics", StringComparison.OrdinalIgnoreCase))
            {
                var document = BphysicsService.Load(args[1]);
                Console.WriteLine($"HKCL={document.HkclPath};SubWind={document.SubWindDirection.X:G7},{document.SubWindDirection.Y:G7},{document.SubWindDirection.Z:G7};Frequency={document.SubWindFrequency:G7};Speed={document.SubWindSpeed:G7}");
                foreach (var cloth in document.Cloths)
                {
                    Console.WriteLine($"{cloth.Name}|{cloth.BaseBone}|{cloth.WindFrequency:G7}|{cloth.WindDrag:G7}|{cloth.WindMinSpeed:G7}|{cloth.WindMaxSpeed:G7}|{cloth.SubWindFactorMain:G7}|{cloth.SubWindFactorAdd:G7}|{cloth.WindEnabled}|{cloth.WritebackToLocal}");
                }

                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-conversion-scales", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                foreach (var scale in service.GetBphclConversionScaleSuggestions())
                    Console.WriteLine($"{scale.ClothIndex}|{scale.ClothName}|{scale.DefaultScale:G7}|{scale.SuggestionBasis}");
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-simulation-settings", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                var cloths = service.GetClothSummaries();
                for (var clothIndex = 0; clothIndex < cloths.Count; clothIndex++)
                {
                    var settings = service.GetSimulationSettings(clothIndex);
                    Console.WriteLine(
                        $"{clothIndex}|{cloths[clothIndex]}|gravity={settings.GravityX:G7},{settings.GravityY:G7},{settings.GravityZ:G7}|" +
                        $"damping={settings.DampingPerSecond:G7}|collisionTolerance={settings.CollisionTolerance:G7}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-plane-colliders", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                var cloths = service.GetClothSummaries();
                for (var clothIndex = 0; clothIndex < cloths.Count; clothIndex++)
                {
                    var options = service.GetParticleColliderOptions(clothIndex);
                    var particles = service.GetParticleRows(clothIndex);
                    foreach (var plane in service.GetColliderRows(clothIndex).Where(row => row.IsPlane))
                    {
                        var option = options.FirstOrDefault(candidate => candidate.ColliderIndex == plane.Index);
                        var affected = option == null || option.BitIndex >= 31
                            ? Array.Empty<int>()
                            : particles.Where(particle => (particle.CollisionMask & (1u << option.BitIndex)) != 0)
                                .Select(particle => particle.Index)
                                .ToArray();
                        Console.WriteLine(
                            $"{clothIndex}|{service.GetClothName(clothIndex)}|plane={plane.Index}:{plane.Name}|" +
                            $"slot={(option == null ? "none" : option.BitIndex)}|affected=[{string.Join(',', affected)}]|" +
                            $"point={plane.StartX:G6},{plane.StartY:G6},{plane.StartZ:G6}|" +
                            $"normal={plane.PlaneNormalX:G6},{plane.PlaneNormalY:G6},{plane.PlaneNormalZ:G6}");
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-collider-masks", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                var cloths = service.GetClothSummaries();
                for (var clothIndex = 0; clothIndex < cloths.Count; clothIndex++)
                {
                    var particles = service.GetParticleRows(clothIndex);
                    foreach (var option in service.GetParticleColliderOptions(clothIndex))
                    {
                        var affected = option.BitIndex >= 31
                            ? Array.Empty<int>()
                            : particles.Where(particle => (particle.CollisionMask & (1u << option.BitIndex)) != 0)
                                .Select(particle => particle.Index)
                                .ToArray();
                        Console.WriteLine(
                            $"{clothIndex}|{service.GetClothName(clothIndex)}|slot={option.BitIndex}|" +
                            $"collider={option.ColliderIndex}:{option.Name}|affected=[{string.Join(',', affected)}]");
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-collider-shapes", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                {
                    foreach (var collider in service.GetColliderRows(clothIndex))
                    {
                        Console.WriteLine(
                            $"{clothIndex}|{service.GetClothName(clothIndex)}|{collider.Index}|{collider.Name}|" +
                            $"{collider.ShapeType}|bone={collider.BoneIndex}|radius={collider.Radius:G8}|" +
                            $"start={collider.StartX:G8},{collider.StartY:G8},{collider.StartZ:G8}|" +
                            $"end={collider.EndX:G8},{collider.EndY:G8},{collider.EndZ:G8}");
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-local-ranges", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                {
                    var particleCount = service.GetParticleRows(clothIndex).Count;
                    for (var particleIndex = 0; particleIndex < particleCount; particleIndex++)
                    {
                        foreach (var relationship in service.GetParticleRelationships(clothIndex, particleIndex)
                                     .Where(row => row.MaximumDistance.HasValue))
                        {
                            Console.WriteLine(
                                $"{clothIndex}|{service.GetClothName(clothIndex)}|particle={particleIndex}|" +
                                $"{relationship.Kind}:{relationship.Name}|max={relationship.MaximumDistance!.Value:G8}");
                        }
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-particle-relationships", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                {
                    var particleCount = service.GetParticleRows(clothIndex).Count;
                    for (var particleIndex = 0; particleIndex < particleCount; particleIndex++)
                    {
                        foreach (var relationship in service.GetParticleRelationships(clothIndex, particleIndex))
                        {
                            Console.WriteLine(
                                $"{clothIndex}|{service.GetClothName(clothIndex)}|particle={particleIndex}|" +
                                $"{relationship.Kind}:{relationship.Name}|other={relationship.Particles}|{relationship.Details}");
                        }
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-collider-bindings", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                foreach (var cloth in service.GetClothSummaries().Select((summary, index) => (summary, index)))
                {
                    foreach (var line in service.GetColliderBindingDiagnostics(cloth.index))
                        Console.WriteLine($"{cloth.index}|{cloth.summary}|{line}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--validate-collider-roundtrip", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                    service.UpdateColliderRows(service.GetColliderRows(clothIndex));

                foreach (var cloth in service.GetClothSummaries().Select((summary, index) => (summary, index)))
                {
                    foreach (var line in service.GetColliderBindingDiagnostics(cloth.index))
                        Console.WriteLine($"{cloth.index}|{cloth.summary}|{line}");
                }
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            if (args.Length > 0)
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "PhysicsTool-command-error.txt"),
                    ex.ToString());
                return;
            }

            MessageBox.Show(
                ex.ToString(),
                "PhysicsTool startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

