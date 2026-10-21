using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private async Task MatchBonesToModel()
    {
        if (!SelectionEditable || _importedModel == null)
            return;

        Session.Document.ValidateClothBoneFit(Session.ClothIndex);
        var choices = MatchModelBonesWindow.Choices(_viewport.Data.Bones, BoundImportedModel.Rest);
        await new MatchModelBonesWindow(choices, ApplyModelBoneMatch).ShowDialog(this);
    }

    internal void ApplyModelBoneMatch(IReadOnlyDictionary<int, Matrix4x4> targets)
    {
        if (!SelectionEditable || _importedModel == null)
            throw new InvalidOperationException("Stop playback and simulation before matching bones.");

        Edit((document, cloth) =>
        {
            document.MatchClothBonesToModel(cloth, targets);

            // Keep the matched rest pose with the undo snapshot so simulation does
            // not apply the original model-to-cloth offset a second time.
            var bones = document.GetParticlePreview(cloth).Bones
                .GroupBy(bone => bone.Name[(bone.Name.LastIndexOf(':') + 1)..], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key,
                    group => Rendering.ClothModelBinding.Matrix(group.First()), StringComparer.OrdinalIgnoreCase);
            var references = Session.ModelBoneReferences.ToDictionary(pair => pair.Key, pair => pair.Value);
            references[Session.SelectedEntryId] = new(_importedModel.Rest.SourcePath, bones);
            Session.ModelBoneReferences = references;
        });

        _status.Text = "Matched cloth bones to the model.";
    }
}
