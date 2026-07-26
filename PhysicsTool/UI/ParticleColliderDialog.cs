using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace HKCLTool;

internal sealed class ParticleColliderDialog : Form
{
    private readonly CheckedListBox _colliders = new();
    private readonly IReadOnlyList<ParticleColliderOption> _options;

    private ParticleColliderDialog(
        string particleLabel,
        IReadOnlyList<ParticleColliderOption> options,
        uint currentMask)
    {
        _options = options;
        Text = $"Colliders for {particleLabel}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(430, 300);
        Size = new Size(510, 440);
        BackColor = Color.FromArgb(42, 42, 42);
        ForeColor = Color.Gainsboro;

        var description = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(10, 8, 10, 4),
            Text = "Select the colliders this particle should collide with. Only colliders assigned to this cloth are listed.",
            ForeColor = Color.Gainsboro
        };

        _colliders.Dock = DockStyle.Fill;
        _colliders.CheckOnClick = true;
        _colliders.BackColor = Color.FromArgb(46, 46, 46);
        _colliders.ForeColor = Color.Gainsboro;
        _colliders.BorderStyle = BorderStyle.FixedSingle;
        foreach (var option in options)
        {
            var listIndex = _colliders.Items.Add(new ColliderChoice(option));
            _colliders.SetItemChecked(listIndex, (currentMask & (1u << option.BitIndex)) != 0);
        }

        var cancel = CreateButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        var apply = CreateButton("Apply");
        apply.DialogResult = DialogResult.OK;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);

        Controls.Add(_colliders);
        Controls.Add(description);
        Controls.Add(buttons);
        AcceptButton = apply;
        CancelButton = cancel;
    }

    public static bool TryChoose(
        IWin32Window owner,
        string particleLabel,
        IReadOnlyList<ParticleColliderOption> options,
        uint currentMask,
        out uint selectedColliderBits)
    {
        using var dialog = new ParticleColliderDialog(particleLabel, options, currentMask);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            selectedColliderBits = 0;
            return false;
        }

        selectedColliderBits = 0;
        foreach (var checkedItem in dialog._colliders.CheckedItems.OfType<ColliderChoice>())
            selectedColliderBits |= 1u << checkedItem.Option.BitIndex;
        return true;
    }

    private static Button CreateButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(64, 64, 64),
            ForeColor = Color.Gainsboro
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(115, 115, 115);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(82, 82, 82);
        return button;
    }

    private sealed class ColliderChoice
    {
        public ColliderChoice(ParticleColliderOption option) => Option = option;

        public ParticleColliderOption Option { get; }

        public override string ToString() => $"{Option.BitIndex}: {Option.Name}";
    }
}
