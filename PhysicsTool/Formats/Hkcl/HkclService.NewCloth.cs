using HKX2;
using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    // A donor-free authoring draft. Model skinning and output-bone bindings are deliberately absent.
    public static HkclService CreateBlankCloth(string name, bool bphcl)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A cloth name is required.");
        var sim = new hclSimClothData
        {
            m_name = "Simulation", m_simulationInfo = new() { m_gravity = new Vector4(0, -9.8f, 0, 0), m_globalDampingPerSecond = .1f, m_collisionTolerance = .01f },
            m_particleDatas = new() { new() { m_mass = .1f, m_invMass = 10, m_radius = .01f, m_friction = .1f } },
            m_fixedParticles = new(), m_triangleIndices = new(), m_triangleFlips = new(),
            m_totalMass = .1f, m_maxParticleRadius = .01f,
            m_collidableTransformMap = new() { m_transformSetIndex = -1, m_transformIndices = new(), m_offsets = new() },
            m_perInstanceCollidables = new(), m_staticConstraintSets = new() { new hclStandardLinkConstraintSet { m_name = "Distance links", m_links = new() } },
            m_antiPinchConstraintSets = new(), m_actions = new(), m_staticCollisionMasks = new() { 0 },
            m_perParticlePinchDetectionEnabledFlags = new() { false }, m_collidablePinchingDatas = new(),
            m_simClothPoses = new() { new() { m_name = "Default", m_positions = new() { new Vector4(0, 0, 0, 1) } } },
            m_landscapeCollisionData = new() { m_landscapeRadius = .05f, m_stuckParticlesStretchFactorSq = 9 },
            m_transferMotionData = new() { m_transformSetIndex = uint.MaxValue, m_transformIndex = uint.MaxValue }
        };
        var cloth = new hclClothData
        {
            m_name = name.Trim(), m_simClothDatas = new() { sim }, m_actions = new(), m_targetPlatform = (Platform)8192,
            m_transformSetDefinitions = new() { new() { m_name = "Transforms", m_type = 0, m_numTransforms = 1 } },
            m_bufferDefinitions = new()
            {
                new hclBufferDefinition { m_name = "Current", m_type = 1, m_subType = 0, m_numVertices = 1, m_bufferLayout = CreateFreshBufferLayout(false) },
                new hclBufferDefinition { m_name = "Previous", m_type = 2, m_subType = 0, m_numVertices = 1, m_bufferLayout = CreateFreshBufferLayout(false) }
            },
            m_operators = new() { new hclSimulateOperator { m_name = "Simulate", m_simClothIndex = 0, m_subSteps = 1, m_numberOfSolveIterations = 4, m_constraintExecution = new() { 0, -1 } } },
            m_clothStateDatas = new()
            {
                new() { m_name = "Simulate", m_operators = new() { 0 }, m_usedSimCloths = new() { 0 }, m_usedBuffers = new(), m_usedTransformSets = new() }
            }
        };
        var skeleton = new hkaSkeleton
        {
            m_name = name.Trim(), m_bones = new() { new() { m_name = "Root", m_lockTranslation = false } },
            m_parentIndices = new() { -1 },
            m_referencePose = new() { new Matrix4x4(0,0,0,0, 0,0,0,1, 1,1,1,0, 0,0,0,1) },
            m_referenceFloats = new(), m_floatSlots = new(), m_localFrames = new(), m_partitions = new()
        };
        var service = new HkclService
        {
            _root = new hkRootLevelContainer { m_namedVariants = new()
            {
                new() { m_name = "Cloth Container", m_className = "hclClothContainer", m_variant = new hclClothContainer { m_clothDatas = new() { cloth }, m_collidables = new() } },
                new() { m_name = "Animation Container", m_className = "hkaAnimationContainer", m_variant = new hkaAnimationContainer { m_skeletons = new() { skeleton }, m_animations = new(), m_bindings = new(), m_attachments = new(), m_skins = new() } }
            } }
        };
        if (bphcl)
        {
            var graph = HkclToBphclGameReadyGraphBuilder.Build(service);
            var native = NativeBphclDocument.Parse(NativeBphclStandaloneGraphWriter.WriteGameReady(graph));
            service._root = null;
            service._bphcl = CreateBphclSummary(native, "NewCloth.bphcl");
        }
        return service;
    }
}
