namespace HKCLTool;

internal sealed record ClothHelpTopic(string Title, string Body)
{
    public override string ToString() => Title;
}

internal sealed class ClothDocumentationDialog : LocalizedForm
{
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search documentation", AccessibleName = "Search documentation" };
    private readonly ListBox _topics = new() { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, HorizontalScrollbar = true, AccessibleName = "Documentation topics" };
    private readonly RichTextBox _article = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical, AccessibleName = "Documentation article" };
    private readonly Label _results = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Font _headingFont;
    internal static IReadOnlyList<ClothHelpTopic> Topics { get; } = BuildTopics();

    public ClothDocumentationDialog()
    {
        Text = "Cloth documentation";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(780, 500);
        BackColor = Color.FromArgb(48, 48, 48);
        ForeColor = Color.Gainsboro;
        _headingFont = new Font(Font.FontFamily, 15, FontStyle.Bold);
        _search.BackColor = Color.FromArgb(60, 60, 60); _search.ForeColor = ForeColor;
        _topics.BackColor = BackColor; _topics.ForeColor = ForeColor;
        _article.BackColor = Color.FromArgb(40, 40, 40); _article.ForeColor = ForeColor;
        _article.Font = new Font(Font.FontFamily, 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _search.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(new Label { Text = "Search", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 12) }, 0, 0);
        layout.Controls.Add(_search, 1, 0);
        _topics.Margin = new Padding(0, 0, 12, 0); layout.Controls.Add(_topics, 0, 1);
        var articlePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = _article.BackColor, Margin = Padding.Empty };
        articlePanel.Controls.Add(_article); layout.Controls.Add(articlePanel, 1, 1);
        layout.Controls.Add(_results, 0, 2);
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
        layout.Controls.Add(close, 1, 2); CancelButton = close;
        Controls.Add(layout);
        _search.TextChanged += (_, _) => FilterTopics();
        _topics.SelectedIndexChanged += (_, _) => ShowTopic();
        FilterTopics();
    }

    protected override void ApplyLocalization()
    {
        base.ApplyLocalization();
        // Localization assigns Text and clears rich-text runs, including on first load.
        ShowTopic();
    }

    private void FilterTopics()
    {
        var selected = _topics.SelectedItem as ClothHelpTopic;
        var terms = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = Topics.Where(t => terms.All(term => (t.Title + " " + t.Body).Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        _topics.BeginUpdate();
        try
        {
            _topics.Items.Clear(); _topics.Items.AddRange(matches);
            if (matches.Length > 0) _topics.SelectedIndex = Math.Max(0, Array.IndexOf(matches, selected));
        }
        finally { _topics.EndUpdate(); }
        _results.Text = matches.Length == 1 ? "1 topic" : $"{matches.Length} topics";
        ShowTopic();
    }

    private void ShowTopic()
    {
        _article.Clear();
        if (_topics.SelectedItem is not ClothHelpTopic topic)
        {
            _article.Text = "No matching topics.";
            return;
        }
        _article.SelectionFont = _headingFont;
        _article.SelectionColor = Color.LightSteelBlue;
        _article.AppendText(topic.Title + "\n\n");
        _article.SelectionFont = _article.Font;
        _article.SelectionColor = ForeColor;
        _article.AppendText(topic.Body.Replace("\r\n", "\n"));
        _article.Select(0, 0); _article.ScrollToCaret();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _headingFont.Dispose(); _article.Font.Dispose(); }
        base.Dispose(disposing);
    }

    private static IReadOnlyList<ClothHelpTopic> BuildTopics()
    {
        var topics = new List<ClothHelpTopic>
        {
            new("Start here", "Cloth is a particle network, not just a set of bones. Particles carry motion; constraints connect or limit them; colliders keep them away from the body. Input bindings provide animated anchors, while output mappings drive model bones from the cloth surface.\n\nHKCL is BotW cloth (Wii U or Switch). BPHCL is TotK cloth. Related formats do not have identical layouts or tuning. BPHYSSB/BPHHB helper-bone graphs are separate from cloth. HKRG is ragdoll data.\n\nWork on a copy, save often, reopen the saved file, and test in-game. A valid file and a convincing preview do not guarantee correct actor registration or identical game behavior."),
            new("Build a blank cloth", "1. Choose New and select Wii U HKCL, Switch HKCL, or TotK BPHCL. A donor-free draft starts with one particle and a Root bone.\n\n2. Add particles and place them manually. Choose anchor particles and enable Fixed where appropriate. Avoid stacking particles at identical positions when creating faces.\n\n3. Select two particles and use Add constraint or F. Distance links form the structure; bend/stretch links can limit folding between non-neighboring particles.\n\n4. Select three non-collinear particles and press F to create a triangle and its edge links. Triangle topology can be needed for cloth-to-bone output, but creating a face does not create that output binding.\n\n5. Save and reopen to inspect the result. A blank draft is UNBOUND: model skinning, output-bone mappings, and actor registration are not automatically created. Loading a reference model alone does not bind the draft to it.\n\nNative BPHCL preview simulation, bone creation/deletion, and collider creation remain unavailable. A BPHCL draft is not a completed game-ready authoring workflow."),
            new("Particles, mass and Fixed", "Position is authored rest/reference geometry, not a saved simulation result. Radius controls particle contact size; friction influences contact response. Change only a few related values at a time.\n\nFixed particles are anchors. Their effective inverse mass is zero and the solver does not move them like free particles. Animation can still move an anchor through a valid binding.\n\nFor a free particle with positive mass, inverse mass is its reciprocal. Lower mass means greater inverse mass and more movement under mass-weighted corrections. There is no verified universal BotW/TotK mass conversion scale.\n\nNative standard/bend stiffness is not a percentage independent of mass. Changing mass alone can change how a constraint behaves. The constraint dialog uses geometry-based lengths and mass-aware defaults; these are starting points, not universal in-game presets.\n\nBoth fixed endpoints cannot respond to a link. Zero stiffness also means no corrective effect."),
            new("Selection, edits and undo", "Select a particle to inspect its relationships on the right. Add constraint can also choose particle indices explicitly. With two particles selected, F opens the constraint dialog; with three it creates a triangle. Delete acts on the active editor selection. Remove constraint removes the selected supported relationship.\n\nPositions can be edited in fields or through viewport transforms. Undo/redo restore supported document edits; keep file backups as well.\n\nStop simulation before changing topology. A disabled Remove button can also mean no relationship is selected or the selected relationship cannot safely be removed.\n\nParticle deletion also removes dependent constraints, triangles, and output-bone triangle mappings where supported. It is not just hiding a point. Native BPHCL rejects unsupported packed virtual-contact or operator layouts without changing the file."),
            new("Constraint troubleshooting", "Only distance, directed stretch, and bend/stretch constraints can currently be created. The other catalog entries explain existing game data and are inspect-only.\n\nAdd requires two different particles in the same cloth and a positive rest/maximum length. Bend minimum must not exceed maximum. Coincident endpoints produce a zero default length: move the points apart or enter a meaningful positive length.\n\nA duplicate link in the target active set is rejected. Stretch links are directional: A-to-B and B-to-A are different. Standard and bend links treat reversed endpoints as the same pair.\n\nAn accepted link may do nothing: both particles might be fixed, the driven endpoint B of a stretch link may be fixed, stiffness may be zero, or the particles may already satisfy its length/range. A stretch limit permits compression.\n\nExisting files can have multiple constraint sets and simulation stages. New supported sets are inserted into applicable execution schedules; collision calls and authored order are retained. Creating more links is not automatically better: redundant or conflicting constraints can overconstrain a cloth.\n\nIf a topology operation reports an unsupported layout, preserve the original and report the file/cloth and exact operation. Do not repeatedly convert formats simply to bypass the restriction."),
            new("Triangles and output bones", "A triangle is three particle indices defining a surface face. Separate particles at the same position can produce a collapsed face, which cannot provide a stable orientation. The native BPHCL triangle command rejects degenerate geometry.\n\nOrdinary faces and packed virtual-collision data are different. Virtual contact points extend collision sampling beyond the stored particles and require additional lookup/topology records. The editor does not generate these records for new faces.\n\nSome cloth-to-bone deformers derive a bone frame from an authored triangle plus a local transform. Removing a triangle can remove its output mapping; adding another triangle does not automatically reconnect that bone.\n\nParticles moving correctly while the model remains static often points to missing or mismatched output mappings, skeleton names/indices, or the wrong installed resource, rather than insufficient constraint strength."),
            new("Collider shapes", "Sphere: a center and radius, useful for compact rounded regions such as joints.\n\nCapsule: two endpoints and a radius, useful for limbs or elongated body regions. Avoid excessively large capsules that force cloth far from its intended rest position.\n\nPlane: a surface described by a normal and offset. Its orientation matters. It is not a finite mesh and is not a substitute for a whole body shape.\n\nOther native shapes may be readable without being fully editable or supported by the preview. Tapered capsules can be moved/rotated as a whole, but their endpoint/radius reshaping remains restricted.\n\nHKCL supports creating the exposed basic shapes. Native BPHCL collider creation remains unsupported; existing supported colliders can be edited or removed from a cloth. Actor collision generation (HKRB/HKSC/polytope) is a separate workflow, not a way to add cloth-body colliders."),
            new("Collider binding and masks", "A cloth collider needs both geometry and the correct transform binding. Moving a bone or its ancestor carries attached colliders in supported edit paths. Scaling/reflection can make the transform non-rigid and is rejected where the native collider layout cannot represent it safely.\n\nParticles do not necessarily collide with every visible collider. Use the particle's Colliders selector to choose participating colliders; it maintains the native collision mask. Check this before increasing stiffness or radius to fix penetration.\n\nNative BPHCL collider deletion removes the selected cloth's references and updates masks and simulation selections. The outer collider object may be retained because another cloth shares it.\n\nFriction affects motion along a contact surface; radius affects separation. Overlapping colliders, anchors inside colliders, or overly large particle radii can produce persistent corrections or jitter. First inspect the rest placement and masks.\n\nLandscape/self collision and specialized pinch recovery are not fully reproduced by the preview. Visibility controls do not enable or disable collision participation."),
            new("Bones, weights and Attach", "Input bones carry animation into the cloth. Output bones receive cloth deformation. These roles can share a skeleton but are not interchangeable. A visually nearby bone is not automatically the correct binding.\n\nParticles follow bones applies supported authored bone-weighted relationships during edits. Unweighted particles cannot infer a bone solely from proximity. A fixed particle still needs the appropriate reference mapping to follow the intended animated anchor.\n\nUse Attach for the supported selected particle/bone workflow, then inspect the saved reference positions and test animation. Enabling Fixed by itself does not create every model or actor binding.\n\nHelper/support-bone groups are separate motion graphs, not ordinary cloth constraints. Removing a helper group does not repair missing cloth-to-model output mappings.\n\nImported FBX/BFRES models and their animations are session reference data. Editing cloth does not automatically rewrite the imported model, its mesh weights, or the game's actor pack."),
            new("Simulation and animation", "Run Simulation is a preview, not the game's full actor runtime. Stop it before editing topology. Simulated motion is not written back as authored particle positions.\n\nFixed inputs, constraints, and moving colliders are evaluated by the supported preview paths. Full Havok operator/state execution, specialized recovery, and all collision branches are not reproduced. Native BPHCL simulation is intentionally unavailable.\n\nSettings offers animation playback at 30 FPS (0.5x) or 60 FPS (1x). These are playback choices, not automatic retuning of cloth masses or solver iterations. The timeline and Loop option control animation transport.\n\nWind is an optional preview approximation, not a claim to match every gameplay wind field. Start with wind off when diagnosing bindings, rest geometry, or contacts.\n\nFor instability, check anchors inside colliders, collapsed triangles, conflicting lengths, extreme inverse masses, and wrong model/skeleton bindings before increasing solver settings. Validate against the correct in-game actor and upgrade variant."),
            new("Save, conversion and portability", "Keep an untouched original. Save a separate edited file and reopen it before installing. Back up the matching model and actor resources too. Armor upgrade variants may load different files even when they look similar.\n\nWii U and Switch HKCL use their respective output platform options. BPHCL has native storage and TYPE metadata; it is not merely an HKCL with a different extension.\n\nConversion preserves supported graphs but can require asset-specific mass/constraint tuning. There is no universal verified scale that makes every BotW and TotK cloth equivalent. Structural validation and successful save/reload do not prove identical in-game simulation.\n\nNew particles/faces do not automatically gain model-skin bindings, output-bone mappings, or virtual contact points. A blank file is not ready for an actor simply because it saves.\n\nDistribute the complete Release folder with its required dependencies, rather than copying only the executable."),
            new("BPHCL support and limits", "Supported native authoring includes particle add/copy, fixed/free and numeric edits, supported bone/collider value edits, masks, three basic link families, ordinary triangles, and selected relationship removal.\n\nParticle deletion works only where all dependent indices can be remapped safely. Packed virtual-contact data and unsupported operators/constraints cause an explicit no-change rejection. Deletion can remove affected output-bone mappings.\n\nCollider removal is cloth-local and preserves shared outer ownership. Bone creation/deletion, collider creation, automatic packed virtual-contact rebuilding, and native BPHCL preview simulation remain unsupported.\n\nConstraint creation supports distance, directed stretch, and bend/stretch. Existing surface-bending and regular/stiffness local-range records have additional editing/removal support, but creating them from scratch is not available. Other families remain inspect-only for creation.\n\nThese limits are intentional safeguards against corrupting a native graph. Finding a constraint in the game does not mean the editor supports creating or simulating it.")
        };
        topics.Add(new ClothHelpTopic("Cloth simulation settings", "Gravity X/Y/Z is an acceleration vector. Its direction and magnitude affect all free particles; it is not an attachment or material setting.\n\nDamping per second is the native 0..1 fraction of velocity removed over one second. Higher damping reduces motion; it is not the same as contact friction, and should not be used to conceal an incorrect binding.\n\nCollision tolerance is a native contact parameter, separate from particle radius and collider size. Change it conservatively and compare against a known-good cloth; larger is not automatically more accurate.\n\nMotion transfer controls how animated character translation and rotation contribute to cloth motion. Each has speed thresholds and blend values. Lower blends can reduce movement/turn-driven flinging, but can also reduce the response you intended.\n\nThese cloth settings are authored data where editing is supported. Preview Options and Wind are separate preview controls. Native BPHCL cloth-settings editing is restricted; do not assume every HKCL control is available for BPHCL.\n\nTune one category at a time: first placement and bindings, then contact shapes/masks, then lengths and mass-aware stiffness, and finally damping or motion transfer. Always compare saved results in-game."));
        foreach (var kind in ClothConstraintCatalog.Kinds)
        {
            var use = kind.ClassName switch
            {
                "hclStandardLinkConstraintSet" => "Typical use: structural edges between neighboring particles. Set rest length to the intended shape. Both free endpoints can move; mass influences how correction is shared.",
                "hclStretchLinkConstraintSet" => "Typical use: keep a follower within a maximum distance of an anchor. A is the anchor; only B is corrected. Reverse the endpoints to change the driven particle. A compressed link is already valid.",
                "hclBendLinkConstraintSet" => "Typical use: limit folding or extension between non-neighboring particles. Choose minimum and maximum separation, then tune compression and stretch coefficients independently. This is not the four-particle surface-bending family.",
                "hclLocalRangeConstraintSet" => "Typical use: keep cloth near an animated reference surface while allowing bounded motion. Existing reference-buffer and vertex mappings must remain valid; a radius alone is not a complete setup.",
                "hclBendStiffnessConstraintSet" => "Typical use: resist curvature changes across adjacent surface triangles. Ordered topology, rest curvature, and geometric weights are required. Arbitrary four-particle selection is not sufficient.",
                "hclBonePlanesConstraintSet" => "Typical use: restrict a particle relative to a plane that moves with a bone. The transform-set binding and plane orientation determine which region is allowed. This is a constraint, not a general body collider.",
                "hclTransitionConstraintSet" => "Typical use: coordinate animation/simulation transitions using runtime state. It is not an ordinary spring and cannot be made functional by inserting an isolated per-particle record.",
                "hclVolumeConstraint" => "Typical use: retain the shape of a multi-particle volume through a weighted frame. This requires a coherent frame/apply configuration, not a two-point length.",
                "hclCompressibleLinkConstraintSet" => "Allows particles to move freely between compression and rest lengths, correcting movement beyond either limit. Existing records work in the preview; creating new ones is not yet supported.",
                _ => "Specialized recovery for pinched contacts. The surrounding detection and temporal runtime state are essential; it is not a replacement for fixing badly intersecting colliders."
            };
            topics.Add(new ClothHelpTopic(kind.Name, kind.Description + "\n\n" + use + "\n\nCreation: " +
                (kind.CanCreate ? "available in Add constraint." : "inspect-only; not currently supported.") + "\n\nNative class: " + kind.ClassName));
        }
        return topics;
    }
}
