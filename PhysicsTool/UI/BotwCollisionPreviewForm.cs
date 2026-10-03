using System.Drawing;

namespace HKCLTool;

internal sealed class BotwCollisionPreviewForm : LocalizedForm
{
    private readonly ParticlePreviewControl _preview = new() { Dock = DockStyle.Fill, PhysicsOverlayVisible = false };

    public BotwCollisionPreviewForm(VisualModelData model)
    {
        Text = "Collision preview";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 480);
        ClientSize = new Size(960, 720);
        BackColor = Color.FromArgb(48, 48, 48);
        Controls.Add(_preview);
        Shown += (_, _) =>
        {
            _preview.SetData(null);
            _preview.SetVisualModel(model);
            _preview.SetVisualModelDisplaySettings(new VisualModelDisplaySettings { MirrorX = true });
            _preview.FitCameraToVisualModel();
        };
    }
}
