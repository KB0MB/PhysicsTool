#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <fbxsdk.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <unordered_map>
#include <unordered_set>
#include <map>
#include <memory>
#include <string>
#include <vector>

#define PT_EXPORT extern "C" __declspec(dllexport)

struct pt_fbx_uv_set {
    std::string name;
    std::vector<float> coordinates;
};

struct pt_fbx_mesh {
    std::string name, texture_path, specular_texture_path, normal_texture_path, ambient_texture_path;
    std::string texture_uv_set, specular_uv_set, normal_uv_set, ambient_uv_set;
    std::vector<std::uint8_t> texture_data;
    std::vector<float> vertices;
    std::vector<std::uint32_t> indices;
    std::vector<pt_fbx_uv_set> uv_sets;
    std::array<float, 4> base_color{ 0.72f, 0.75f, 0.78f, 1.0f };
    int culling_mode = static_cast<int>(FbxNode::eCullingOff);
};

struct pt_fbx_bone {
    std::string name;
    int parent_index = -1;
    std::array<float, 3> position{};
    std::array<float, 4> rotation{ 0.0f, 0.0f, 0.0f, 1.0f };
    std::array<float, 3> scale{ 1.0f, 1.0f, 1.0f };
};

struct pt_fbx_scene {
    std::vector<pt_fbx_mesh> meshes;
    std::vector<pt_fbx_bone> bones;
    std::filesystem::path extracted_media_directory;
    bool owns_extracted_media = true;
};

struct pt_fbx_animation {
    FbxManager* manager = nullptr;
    FbxScene* scene = nullptr;
    std::vector<FbxNode*> bone_nodes;
    std::vector<std::string> animated_bone_names;
    std::filesystem::path extracted_media_directory;
    double start_frame = 0.0;
    double end_frame = 0.0;
    double frames_per_second = 30.0;
};

namespace {
constexpr std::size_t vertex_stride = 8;

void set_error(char* buffer, std::size_t capacity, const std::string& message)
{
    if (buffer && capacity) strncpy_s(buffer, capacity, message.c_str(), _TRUNCATE);
}

std::string to_utf8(const wchar_t* value)
{
    if (!value || !*value) return {};
    int size = WideCharToMultiByte(CP_UTF8, 0, value, -1, nullptr, 0, nullptr, nullptr);
    if (size <= 1) return {};
    std::string result(static_cast<std::size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, value, -1, result.data(), size, nullptr, nullptr);
    result.resize(static_cast<std::size_t>(size - 1));
    return result;
}

std::filesystem::path create_media_directory()
{
    wchar_t directory[MAX_PATH]{}, candidate[MAX_PATH]{};
    if (!GetTempPathW(MAX_PATH, directory) ||
        !GetTempFileNameW(directory, L"PTF", 0, candidate)) return {};
    DeleteFileW(candidate);
    return CreateDirectoryW(candidate, nullptr) ? candidate : std::filesystem::path{};
}

FbxAMatrix geometry_transform(FbxNode* node)
{
    return FbxAMatrix(
        node->GetGeometricTranslation(FbxNode::eSourcePivot),
        node->GetGeometricRotation(FbxNode::eSourcePivot),
        node->GetGeometricScaling(FbxNode::eSourcePivot));
}

FbxFileTexture* material_texture(FbxSurfaceMaterial* material, const char* property_name)
{
    if (!material) return nullptr;
    FbxProperty property = material->FindProperty(property_name);
    if (!property.IsValid()) return nullptr;
    if (auto* texture = property.GetSrcObject<FbxFileTexture>(0)) return texture;
    if (auto* layered = property.GetSrcObject<FbxLayeredTexture>(0))
        return layered->GetSrcObject<FbxFileTexture>(0);
    return nullptr;
}

FbxFileTexture* diffuse_texture(FbxSurfaceMaterial* material)
{ return material_texture(material, FbxSurfaceMaterial::sDiffuse); }

FbxFileTexture* specular_texture(FbxSurfaceMaterial* material)
{ return material_texture(material, FbxSurfaceMaterial::sSpecular); }

FbxFileTexture* normal_texture(FbxSurfaceMaterial* material)
{
    if (auto* texture = material_texture(material, FbxSurfaceMaterial::sNormalMap)) return texture;
    return material_texture(material, FbxSurfaceMaterial::sBump);
}

FbxFileTexture* ambient_texture(FbxSurfaceMaterial* material)
{
    if (auto* texture = material_texture(material, FbxSurfaceMaterial::sAmbient)) return texture;
    if (auto* texture = material_texture(material, "AO")) return texture;
    return material_texture(material, "Occlusion");
}

std::array<float, 4> material_color(FbxSurfaceMaterial* material)
{
    std::array<float, 4> result{ 0.72f, 0.75f, 0.78f, 1.0f };
    if (!material) return result;
    FbxProperty diffuse = material->FindProperty(FbxSurfaceMaterial::sDiffuse);
    FbxProperty factor = material->FindProperty(FbxSurfaceMaterial::sDiffuseFactor);
    if (diffuse.IsValid()) {
        FbxDouble3 color = diffuse.Get<FbxDouble3>();
        double multiplier = factor.IsValid() ? factor.Get<FbxDouble>() : 1.0;
        for (int i = 0; i < 3; ++i)
            result[i] = static_cast<float>(std::clamp(color[i] * multiplier, 0.0, 1.0));
    }
    FbxProperty transparency = material->FindProperty(FbxSurfaceMaterial::sTransparencyFactor);
    if (transparency.IsValid())
        result[3] = static_cast<float>(std::clamp(1.0 - transparency.Get<FbxDouble>(), 0.0, 1.0));
    return result;
}

int material_index(FbxMesh* mesh, int polygon)
{
    auto* element = mesh->GetElementMaterial();
    if (!element || element->GetIndexArray().GetCount() == 0) return 0;
    return element->GetMappingMode() == FbxGeometryElement::eByPolygon
        ? element->GetIndexArray().GetAt(polygon)
        : element->GetIndexArray().GetAt(0);
}

// A diffuse texture may explicitly select a UV set; using the first set breaks multi-UV meshes.
std::string uv_set_name(FbxMesh* mesh, FbxFileTexture* texture)
{
    if (texture) {
        FbxString selected = texture->UVSet.Get();
        if (selected.GetLen() && mesh->GetElementUV(selected.Buffer())) return selected.Buffer();
    }
    FbxStringList names;
    mesh->GetUVSetNames(names);
    return names.GetCount() ? names.GetStringAt(0) : std::string{};
}


std::string texture_path(FbxFileTexture* texture)
{
    if (!texture) return {};
    if (texture->GetFileName() && *texture->GetFileName()) return texture->GetFileName();
    if (texture->GetRelativeFileName() && *texture->GetRelativeFileName())
        return texture->GetRelativeFileName();
    return {};
}

std::string mesh_name(FbxNode* node, FbxSurfaceMaterial* material, std::size_t part_count)
{
    std::string result = node->GetName();
    if (result.empty() && node->GetMesh()) result = node->GetMesh()->GetName();
    if (result.empty()) result = "Mesh";
    if (part_count > 1 && material && material->GetName() && *material->GetName()) {
        result += "__";
        result += material->GetName();
    }
    return result;
}

FbxVector4 normalized(FbxVector4 value)
{
    double length = std::sqrt(value[0] * value[0] + value[1] * value[1] + value[2] * value[2]);
    if (length > 1.0e-12) {
        value[0] /= length; value[1] /= length; value[2] /= length;
    } else {
        value = FbxVector4(0.0, 1.0, 0.0, 0.0);
    }
    value[3] = 0.0;
    return value;
}

bool reverses_handedness(const FbxAMatrix& matrix)
{
    const double determinant =
        matrix.Get(0, 0) * (matrix.Get(1, 1) * matrix.Get(2, 2) - matrix.Get(1, 2) * matrix.Get(2, 1)) -
        matrix.Get(0, 1) * (matrix.Get(1, 0) * matrix.Get(2, 2) - matrix.Get(1, 2) * matrix.Get(2, 0)) +
        matrix.Get(0, 2) * (matrix.Get(1, 0) * matrix.Get(2, 1) - matrix.Get(1, 1) * matrix.Get(2, 0));
    return determinant < 0.0;
}

struct maya_anim_channel {
    std::string node;
    std::string attribute;
    std::vector<std::pair<double, double>> keys;
};

std::string trim(std::string value)
{
    const auto first = value.find_first_not_of(" \t\r\n");
    if (first == std::string::npos) return {};
    const auto last = value.find_last_not_of(" \t\r\n;");
    return value.substr(first, last - first + 1);
}

std::string normalized_node_name(std::string value)
{
    const auto pipe = value.find_last_of('|');
    if (pipe != std::string::npos) value = value.substr(pipe + 1);
    const auto colon = value.find_last_of(':');
    if (colon != std::string::npos) value = value.substr(colon + 1);
    return value;
}

double sample_channel(const maya_anim_channel& channel, double frame)
{
    if (channel.keys.empty()) return 0.0;
    if (frame <= channel.keys.front().first) return channel.keys.front().second;
    if (frame >= channel.keys.back().first) return channel.keys.back().second;
    const auto upper = std::upper_bound(
        channel.keys.begin(), channel.keys.end(), frame,
        [](double value, const auto& key) { return value < key.first; });
    const auto& right = *upper;
    const auto& left = *(upper - 1);
    const double span = right.first - left.first;
    const double amount = span <= 1.0e-12 ? 0.0 : (frame - left.first) / span;
    return left.second + (right.second - left.second) * amount;
}

bool read_maya_anim(
    const std::string& path,
    std::vector<maya_anim_channel>& channels,
    std::string& linear_unit,
    std::string& time_unit,
    double& start_frame,
    double& end_frame,
    std::string& error)
{
    std::ifstream stream(path);
    if (!stream) {
        error = "The Maya .anim file could not be opened.";
        return false;
    }

    maya_anim_channel* active = nullptr;
    bool reading_keys = false;
    std::string line;
    while (std::getline(stream, line)) {
        const std::string text = trim(line);
        if (text.rfind("linearUnit ", 0) == 0) {
            linear_unit = trim(text.substr(11));
            continue;
        }
        if (text.rfind("timeUnit ", 0) == 0) {
            time_unit = trim(text.substr(9));
            continue;
        }
        if (text.rfind("startTime ", 0) == 0) {
            try { start_frame = std::stod(trim(text.substr(10))); } catch (...) {}
            continue;
        }
        if (text.rfind("endTime ", 0) == 0) {
            try { end_frame = std::stod(trim(text.substr(8))); } catch (...) {}
            continue;
        }
        if (text.rfind("anim ", 0) == 0) {
            std::istringstream words(text);
            std::vector<std::string> parts;
            std::string part;
            while (words >> part) parts.push_back(part);
            active = nullptr;
            reading_keys = false;
            if (parts.size() >= 4) {
                const std::string attribute = parts[2];
                if (attribute == "translateX" || attribute == "translateY" || attribute == "translateZ" ||
                    attribute == "rotateX" || attribute == "rotateY" || attribute == "rotateZ" ||
                    attribute == "scaleX" || attribute == "scaleY" || attribute == "scaleZ") {
                    channels.push_back({ parts[3], attribute, {} });
                    active = &channels.back();
                }
            }
            continue;
        }
        if (active && text.rfind("keys {", 0) == 0) {
            reading_keys = true;
            continue;
        }
        if (!active || !reading_keys) continue;
        if (text == "}") {
            reading_keys = false;
            continue;
        }

        std::istringstream values(text);
        double time = 0.0, value = 0.0;
        if (values >> time >> value)
            active->keys.emplace_back(time, value);
    }
    return true;
}

void collect_nodes(FbxNode* node, std::unordered_map<std::string, FbxNode*>& nodes)
{
    if (!node) return;
    const std::string exact = node->GetName();
    if (!exact.empty()) {
        nodes.try_emplace(exact, node);
        nodes.try_emplace(normalized_node_name(exact), node);
    }
    for (int index = 0; index < node->GetChildCount(); ++index)
        collect_nodes(node->GetChild(index), nodes);
}

bool apply_maya_anim(FbxScene* scene, const std::string& path, double frame, std::string& error)
{
    std::vector<maya_anim_channel> channels;
    std::string linear_unit = "cm";
    std::string time_unit = "ntsc";
    double start_frame = 0.0, end_frame = 0.0;
    if (!read_maya_anim(path, channels, linear_unit, time_unit, start_frame, end_frame, error))
        return false;

    std::unordered_map<std::string, FbxNode*> nodes;
    collect_nodes(scene->GetRootNode(), nodes);
    const double anim_centimeters =
        linear_unit == "m" ? 100.0 :
        linear_unit == "mm" ? 0.1 :
        linear_unit == "in" ? 2.54 :
        linear_unit == "ft" ? 30.48 : 1.0;
    const double scene_centimeters = scene->GetGlobalSettings().GetSystemUnit().GetScaleFactor();
    const double translation_scale = anim_centimeters / std::max(1.0e-12, scene_centimeters);

    for (const auto& channel : channels) {
        auto found = nodes.find(channel.node);
        if (found == nodes.end()) found = nodes.find(normalized_node_name(channel.node));
        if (found == nodes.end() || channel.keys.empty()) continue;
        FbxNode* node = found->second;
        const double value = sample_channel(channel, frame);
        if (channel.attribute.rfind("translate", 0) == 0) {
            FbxDouble3 vector = node->LclTranslation.Get();
            const int axis = channel.attribute.back() == 'X' ? 0 : channel.attribute.back() == 'Y' ? 1 : 2;
            vector[axis] = value * translation_scale;
            node->LclTranslation.Set(vector);
        } else if (channel.attribute.rfind("rotate", 0) == 0) {
            FbxDouble3 vector = node->LclRotation.Get();
            const int axis = channel.attribute.back() == 'X' ? 0 : channel.attribute.back() == 'Y' ? 1 : 2;
            vector[axis] = value;
            node->LclRotation.Set(vector);
        } else if (channel.attribute.rfind("scale", 0) == 0) {
            FbxDouble3 vector = node->LclScaling.Get();
            const int axis = channel.attribute.back() == 'X' ? 0 : channel.attribute.back() == 'Y' ? 1 : 2;
            vector[axis] = value;
            node->LclScaling.Set(vector);
        }
    }

    scene->GetAnimationEvaluator()->Reset();
    return true;
}

double animation_frames_per_second(const std::string& unit)
{
    if (unit == "game") return 15.0;
    if (unit == "film") return 24.0;
    if (unit == "pal") return 25.0;
    if (unit == "ntsc") return 30.0;
    if (unit == "show") return 48.0;
    if (unit == "palf") return 50.0;
    if (unit == "ntscf") return 60.0;
    if (unit.size() > 3 && unit.substr(unit.size() - 3) == "fps") {
        try { return std::max(1.0, std::stod(unit.substr(0, unit.size() - 3))); }
        catch (...) {}
    }
    return 30.0;
}

bool create_animation_curves(
    FbxScene* scene,
    const std::vector<maya_anim_channel>& channels,
    const std::string& linear_unit,
    double frames_per_second,
    std::string& error)
{
    if (!scene || channels.empty()) {
        error = "The Maya .anim file contains no supported transform channels.";
        return false;
    }

    auto* stack = FbxAnimStack::Create(scene, "PhysicsToolAnimation");
    auto* layer = FbxAnimLayer::Create(scene, "BaseLayer");
    if (!stack || !layer) {
        error = "Autodesk FBX SDK could not create the animation layer.";
        return false;
    }
    stack->AddMember(layer);
    scene->SetCurrentAnimationStack(stack);

    std::unordered_map<std::string, FbxNode*> nodes;
    collect_nodes(scene->GetRootNode(), nodes);
    const double anim_centimeters =
        linear_unit == "m" ? 100.0 :
        linear_unit == "mm" ? 0.1 :
        linear_unit == "in" ? 2.54 :
        linear_unit == "ft" ? 30.48 : 1.0;
    const double scene_centimeters = scene->GetGlobalSettings().GetSystemUnit().GetScaleFactor();
    const double translation_scale = anim_centimeters / std::max(1.0e-12, scene_centimeters);

    for (const auto& channel : channels) {
        auto found = nodes.find(channel.node);
        if (found == nodes.end()) found = nodes.find(normalized_node_name(channel.node));
        if (found == nodes.end() || channel.keys.empty()) continue;

        const char axis = channel.attribute.back();
        const char* component = axis == 'X'
            ? FBXSDK_CURVENODE_COMPONENT_X
            : axis == 'Y' ? FBXSDK_CURVENODE_COMPONENT_Y : FBXSDK_CURVENODE_COMPONENT_Z;
        FbxAnimCurve* curve = nullptr;
        double value_scale = 1.0;
        if (channel.attribute.rfind("translate", 0) == 0) {
            curve = found->second->LclTranslation.GetCurve(layer, component, true);
            value_scale = translation_scale;
        } else if (channel.attribute.rfind("rotate", 0) == 0) {
            curve = found->second->LclRotation.GetCurve(layer, component, true);
        } else if (channel.attribute.rfind("scale", 0) == 0) {
            curve = found->second->LclScaling.GetCurve(layer, component, true);
        }
        if (!curve) continue;

        curve->KeyModifyBegin();
        for (const auto& key : channel.keys) {
            FbxTime time;
            time.SetSecondDouble(key.first / frames_per_second);
            const int key_index = curve->KeyAdd(time);
            curve->KeySetValue(key_index, static_cast<float>(key.second * value_scale));
            curve->KeySetInterpolation(key_index, FbxAnimCurveDef::eInterpolationLinear);
        }
        curve->KeyModifyEnd();
    }

    scene->GetAnimationEvaluator()->Reset();
    return true;
}
FbxAMatrix zero_matrix()
{
    FbxAMatrix result;
    for (int row = 0; row < 4; ++row)
        for (int column = 0; column < 4; ++column)
            result[row][column] = 0.0;
    return result;
}

struct pose_context {
    const FbxTime& time;
    std::unordered_map<FbxNode*, FbxAMatrix> overrides;
    std::unordered_map<FbxNode*, FbxAMatrix> resolved;
    std::unordered_map<FbxNode*, bool> affected;
};

bool has_pose_override(FbxNode* node, pose_context* pose)
{
    if (!node || !pose) return false;
    const auto cached = pose->affected.find(node);
    if (cached != pose->affected.end()) return cached->second;
    const bool result = pose->overrides.find(node) != pose->overrides.end() ||
        has_pose_override(node->GetParent(), pose);
    pose->affected.emplace(node, result);
    return result;
}

FbxAMatrix evaluated_global(FbxNode* node, const FbxTime& time, pose_context* pose)
{
    if (!node || !pose || !has_pose_override(node, pose))
        return node ? node->EvaluateGlobalTransform(time) : FbxAMatrix();
    const auto cached = pose->resolved.find(node);
    if (cached != pose->resolved.end()) return cached->second;
    const auto direct = pose->overrides.find(node);
    if (direct != pose->overrides.end()) {
        pose->resolved.emplace(node, direct->second);
        return direct->second;
    }

    FbxNode* parent = node->GetParent();
    const FbxAMatrix original = node->EvaluateGlobalTransform(time);
    if (!parent) {
        pose->resolved.emplace(node, original);
        return original;
    }
    // Preserve the FBX-evaluated child-to-parent transform, including pivots
    // and inherit modes, beneath an overridden helper bone.
    const FbxAMatrix parent_original = parent->EvaluateGlobalTransform(time);
    const FbxAMatrix result = evaluated_global(parent, time, pose) *
        parent_original.Inverse() * original;
    pose->resolved.emplace(node, result);
    return result;
}

FbxAMatrix cluster_deformation(
    FbxNode* mesh_node,
    FbxCluster* cluster,
    const FbxTime& time,
    pose_context* pose)
{
    FbxAMatrix mesh_bind, link_bind;
    cluster->GetTransformMatrix(mesh_bind);
    cluster->GetTransformLinkMatrix(link_bind);
    mesh_bind *= geometry_transform(mesh_node);
    const FbxAMatrix mesh_current = evaluated_global(mesh_node, time, pose);
    const FbxAMatrix link_current = evaluated_global(cluster->GetLink(), time, pose);
    return mesh_current.Inverse() * link_current * link_bind.Inverse() * mesh_bind;
}

struct deformed_control_points {
    std::vector<FbxVector4> positions;
    std::vector<FbxAMatrix> transforms;
    std::vector<double> weights;
};

deformed_control_points deform_control_points(
    FbxNode* node,
    FbxMesh* mesh,
    bool enabled,
    const FbxTime& time,
    pose_context* pose)
{
    const int count = mesh->GetControlPointsCount();
    deformed_control_points result;
    result.positions.assign(mesh->GetControlPoints(), mesh->GetControlPoints() + count);
    result.transforms.assign(static_cast<std::size_t>(count), zero_matrix());
    result.weights.assign(static_cast<std::size_t>(count), 0.0);
    if (!enabled) return result;

    for (int skin_index = 0; skin_index < mesh->GetDeformerCount(FbxDeformer::eSkin); ++skin_index) {
        auto* skin = static_cast<FbxSkin*>(mesh->GetDeformer(skin_index, FbxDeformer::eSkin));
        if (!skin) continue;
        for (int cluster_index = 0; cluster_index < skin->GetClusterCount(); ++cluster_index) {
            FbxCluster* cluster = skin->GetCluster(cluster_index);
            if (!cluster || !cluster->GetLink()) continue;
            const FbxAMatrix transform = cluster_deformation(node, cluster, time, pose);
            const int* indices = cluster->GetControlPointIndices();
            const double* weights = cluster->GetControlPointWeights();
            for (int influence = 0; influence < cluster->GetControlPointIndicesCount(); ++influence) {
                const int point = indices[influence];
                const double weight = weights[influence];
                if (point < 0 || point >= count || weight <= 0.0) continue;
                for (int row = 0; row < 4; ++row)
                    for (int column = 0; column < 4; ++column)
                        result.transforms[point][row][column] = result.transforms[point].Get(row, column) + transform.Get(row, column) * weight;
                result.weights[point] += weight;
            }
        }
    }

    for (int point = 0; point < count; ++point) {
        const double weight = result.weights[point];
        if (weight <= 1.0e-12) continue;
        for (int row = 0; row < 4; ++row)
            for (int column = 0; column < 4; ++column)
                result.transforms[point][row][column] = result.transforms[point].Get(row, column) / weight;
        result.positions[point] = result.transforms[point].MultT(result.positions[point]);
    }
    return result;
}

void append_bones(
    pt_fbx_scene& output,
    FbxNode* node,
    int parent_index,
    const FbxTime& time,
    pose_context* pose = nullptr)
{
    if (!node) return;
    int current_parent = parent_index;
    if (node->GetParent() && node->GetName() && *node->GetName()) {
        const FbxAMatrix world = evaluated_global(node, time, pose);
        const FbxVector4 position = world.GetT();
        const FbxQuaternion rotation = world.GetQ();
        const FbxVector4 scale = world.GetS();
        pt_fbx_bone bone;
        bone.name = node->GetName();
        bone.parent_index = parent_index;
        bone.position = {
            static_cast<float>(position[0]), static_cast<float>(position[1]), static_cast<float>(position[2]) };
        bone.rotation = {
            static_cast<float>(rotation[0]), static_cast<float>(rotation[1]),
            static_cast<float>(rotation[2]), static_cast<float>(rotation[3]) };
        bone.scale = {
            static_cast<float>(scale[0]), static_cast<float>(scale[1]), static_cast<float>(scale[2]) };
        current_parent = static_cast<int>(output.bones.size());
        output.bones.push_back(std::move(bone));
    }
    for (int index = 0; index < node->GetChildCount(); ++index)
        append_bones(output, node->GetChild(index), current_parent, time, pose);
}

void collect_bone_nodes(std::vector<FbxNode*>& output, FbxNode* node)
{
    if (!node) return;
    if (node->GetParent() && node->GetName() && *node->GetName())
        output.push_back(node);
    for (int index = 0; index < node->GetChildCount(); ++index)
        collect_bone_nodes(output, node->GetChild(index));
}

void append_mesh(
    pt_fbx_scene& output,
    FbxNode* node,
    bool apply_skin,
    const FbxTime& time,
    pose_context* pose = nullptr,
    bool dynamic_only = false)
{
    FbxMesh* mesh = node->GetMesh();
    if (!mesh || !mesh->GetPolygonCount()) return;
    if (!mesh->GetElementNormal()) mesh->GenerateNormals(true, false, false);
    const auto deformed = deform_control_points(node, mesh, apply_skin, time, pose);

    std::vector<int> materials;
    for (int polygon = 0; polygon < mesh->GetPolygonCount(); ++polygon) {
        int index = material_index(mesh, polygon);
        if (std::find(materials.begin(), materials.end(), index) == materials.end())
            materials.push_back(index);
    }

    std::map<int, std::size_t> outputs;
    for (int index : materials) {
        FbxSurfaceMaterial* material =
            index >= 0 && index < node->GetMaterialCount() ? node->GetMaterial(index) : nullptr;
        FbxFileTexture* texture = diffuse_texture(material);
        pt_fbx_mesh target;
        target.name = mesh_name(node, material, materials.size());
        if (!dynamic_only) {
            FbxFileTexture* specular = specular_texture(material);
            FbxFileTexture* normal = normal_texture(material);
            FbxFileTexture* ambient = ambient_texture(material);
            target.texture_path = texture_path(texture);
            target.specular_texture_path = texture_path(specular);
            target.normal_texture_path = texture_path(normal);
            target.ambient_texture_path = texture_path(ambient);
            target.texture_uv_set = uv_set_name(mesh, texture);
            target.specular_uv_set = uv_set_name(mesh, specular);
            target.normal_uv_set = uv_set_name(mesh, normal);
            target.ambient_uv_set = uv_set_name(mesh, ambient);
            target.base_color = material_color(material);
            target.culling_mode = static_cast<int>(node->mCullingType);

            FbxStringList uv_names;
            mesh->GetUVSetNames(uv_names);
            for (int uv_index = 0; uv_index < uv_names.GetCount(); ++uv_index) {
                const char* uv_name = uv_names.GetStringAt(uv_index);
                if (uv_name && *uv_name)
                    target.uv_sets.push_back({ uv_name, {} });
            }
        }

        outputs.emplace(index, output.meshes.size());
        output.meshes.push_back(std::move(target));
    }

    FbxAMatrix transform = evaluated_global(node, time, pose) * geometry_transform(node);
    const bool reverse_winding = reverses_handedness(transform);
    FbxAMatrix normal_transform = transform.Inverse().Transpose();
    normal_transform.SetT(FbxVector4(0.0, 0.0, 0.0, 0.0));

    for (int polygon = 0; polygon < mesh->GetPolygonCount(); ++polygon) {
        if (mesh->GetPolygonSize(polygon) != 3) continue;
        int index = material_index(mesh, polygon);
        pt_fbx_mesh& target = output.meshes[outputs.at(index)];
        for (int corner = 0; corner < 3; ++corner) {
            const int source_corner = reverse_winding && corner > 0 ? 3 - corner : corner;
            int control_point = mesh->GetPolygonVertex(polygon, source_corner);
            FbxVector4 position = transform.MultT(deformed.positions[control_point]);
            FbxVector4 normal;
            if (!mesh->GetPolygonVertexNormal(polygon, source_corner, normal))
                normal = FbxVector4(0.0, 1.0, 0.0, 0.0);
            if (apply_skin && deformed.weights[control_point] > 1.0e-12) {
                FbxAMatrix skin_normal = deformed.transforms[control_point].Inverse().Transpose();
                skin_normal.SetT(FbxVector4(0.0, 0.0, 0.0, 0.0));
                normal = skin_normal.MultT(normal);
            }
            normal = normalized(normal_transform.MultT(normal));
            FbxVector2 uv(0.0, 0.0);
            bool unmapped = false;
            if (!dynamic_only && !target.texture_uv_set.empty())
                mesh->GetPolygonVertexUV(polygon, source_corner, target.texture_uv_set.c_str(), uv, unmapped);
            target.vertices.insert(target.vertices.end(), {
                static_cast<float>(position[0]), static_cast<float>(position[1]), static_cast<float>(position[2]),
                static_cast<float>(normal[0]), static_cast<float>(normal[1]), static_cast<float>(normal[2]),
                static_cast<float>(uv[0]), static_cast<float>(uv[1])
            });
            for (auto& uv_set : target.uv_sets) {
                FbxVector2 coordinates(0.0, 0.0);
                bool uv_unmapped = false;
                mesh->GetPolygonVertexUV(
                    polygon, source_corner, uv_set.name.c_str(), coordinates, uv_unmapped);
                uv_set.coordinates.push_back(static_cast<float>(coordinates[0]));
                uv_set.coordinates.push_back(static_cast<float>(coordinates[1]));
            }
            if (!dynamic_only)
                target.indices.push_back(static_cast<std::uint32_t>(target.indices.size()));
        }
    }
}

void visit(
    pt_fbx_scene& output,
    FbxNode* node,
    bool apply_skin,
    const FbxTime& time,
    pose_context* pose = nullptr,
    bool dynamic_only = false)
{
    if (!node) return;
    append_mesh(output, node, apply_skin, time, pose, dynamic_only);
    for (int i = 0; i < node->GetChildCount(); ++i)
        visit(output, node->GetChild(i), apply_skin, time, pose, dynamic_only);
}
}

static pt_fbx_scene* load_fbx_impl(const wchar_t* path, const wchar_t* animation_path, double frame, char* errors, std::size_t capacity)
{
    try {
        if (!path || !*path) { set_error(errors, capacity, "The FBX path is empty."); return nullptr; }
        std::string source_path = to_utf8(path);
        if (source_path.empty()) {
            set_error(errors, capacity, "The FBX path could not be converted to UTF-8.");
            return nullptr;
        }

        std::unique_ptr<FbxManager, void(*)(FbxManager*)> manager(
            FbxManager::Create(), [](FbxManager* value) { if (value) value->Destroy(); });
        if (!manager) {
            set_error(errors, capacity, "Autodesk FBX SDK could not create its manager.");
            return nullptr;
        }
        FbxIOSettings* settings = FbxIOSettings::Create(manager.get(), IOSROOT);
        manager->SetIOSettings(settings);
        settings->SetBoolProp(IMP_FBX_MATERIAL, true);
        settings->SetBoolProp(IMP_FBX_TEXTURE, true);
        settings->SetBoolProp(IMP_FBX_LINK, true);
        settings->SetBoolProp(IMP_FBX_SHAPE, false);
        settings->SetBoolProp(IMP_FBX_ANIMATION, false);
        settings->SetBoolProp(IMP_FBX_EXTRACT_EMBEDDED_DATA, true);

        std::unique_ptr<FbxImporter, void(*)(FbxImporter*)> importer(
            FbxImporter::Create(manager.get(), ""),
            [](FbxImporter* value) { if (value) value->Destroy(); });
        if (!importer->Initialize(source_path.c_str(), -1, settings)) {
            set_error(errors, capacity, importer->GetStatus().GetErrorString());
            return nullptr;
        }

        auto result = std::make_unique<pt_fbx_scene>();
        result->extracted_media_directory = create_media_directory();
        if (!result->extracted_media_directory.empty()) {
            std::string media = result->extracted_media_directory.u8string();
            importer->SetEmbeddingExtractionFolder(media.c_str());
        }

        FbxScene* scene = FbxScene::Create(manager.get(), "PhysicsToolVisualModel");
        if (!scene || !importer->Import(scene)) {
            set_error(errors, capacity, importer->GetStatus().GetErrorString());
            if (scene) scene->Destroy();
            return nullptr;
        }
        const bool animated = animation_path && *animation_path;
        if (animated) {
            std::string animation_source = to_utf8(animation_path);
            std::string animation_error;
            if (animation_source.empty() ||
                !apply_maya_anim(scene, animation_source, frame, animation_error)) {
                scene->Destroy();
                set_error(errors, capacity,
                    animation_error.empty() ? "The Maya .anim path could not be converted to UTF-8." : animation_error);
                return nullptr;
            }
        }

        FbxAxisSystem::OpenGL.ConvertScene(scene);
        FbxSystemUnit::m.ConvertScene(scene);
        FbxGeometryConverter converter(manager.get());
        if (!converter.Triangulate(scene, true, false)) {
            scene->Destroy();
            set_error(errors, capacity, "Autodesk FBX SDK could not triangulate every mesh.");
            return nullptr;
        }

        FbxTime sample_time;
        sample_time.SetSecondDouble(0.0);
        append_bones(*result, scene->GetRootNode(), -1, sample_time);
        visit(*result, scene->GetRootNode(), animated, sample_time);
        scene->Destroy();
        result->meshes.erase(std::remove_if(result->meshes.begin(), result->meshes.end(),
            [](const pt_fbx_mesh& mesh) { return mesh.indices.empty(); }), result->meshes.end());
        if (result->meshes.empty()) {
            set_error(errors, capacity, "The FBX contained no triangle meshes PhysicsTool could display.");
            return nullptr;
        }
        return result.release();
    } catch (const std::exception& exception) {
        set_error(errors, capacity, exception.what());
        return nullptr;
    } catch (...) {
        set_error(errors, capacity, "Unknown Autodesk FBX SDK import error.");
        return nullptr;
    }
}

static void release_animation(pt_fbx_animation* animation)
{
    if (!animation) return;
    const auto media = animation->extracted_media_directory;
    if (animation->manager) animation->manager->Destroy();
    delete animation;
    std::error_code ignored;
    if (!media.empty()) std::filesystem::remove_all(media, ignored);
}

PT_EXPORT pt_fbx_animation* pt_fbx_animation_open(
    const wchar_t* path,
    const wchar_t* animation_path,
    char* errors,
    std::size_t capacity)
{
    pt_fbx_animation* animation = nullptr;
    try {
        if (!path || !*path) {
            set_error(errors, capacity, "The FBX path is required.");
            return nullptr;
        }
        const std::string source_path = to_utf8(path);
        const bool has_animation = animation_path && *animation_path;
        const std::string animation_source = has_animation
            ? to_utf8(animation_path)
            : std::string();
        if (source_path.empty() || (has_animation && animation_source.empty())) {
            set_error(errors, capacity, "The FBX or Maya .anim path could not be converted to UTF-8.");
            return nullptr;
        }

        std::vector<maya_anim_channel> channels;
        std::string linear_unit = "cm", time_unit = "ntsc", animation_error;
        double start_frame = 0.0, end_frame = 0.0;
        if (has_animation && !read_maya_anim(
                animation_source,
                channels,
                linear_unit,
                time_unit,
                start_frame,
                end_frame,
                animation_error)) {
            set_error(errors, capacity, animation_error);
            return nullptr;
        }
        if (has_animation && end_frame < start_frame) std::swap(start_frame, end_frame);
        if (has_animation && end_frame <= start_frame) {
            bool found_key = false;
            for (const auto& channel : channels) {
                for (const auto& key : channel.keys) {
                    if (!found_key) {
                        start_frame = end_frame = key.first;
                        found_key = true;
                    } else {
                        start_frame = std::min(start_frame, key.first);
                        end_frame = std::max(end_frame, key.first);
                    }
                }
            }
        }

        animation = new pt_fbx_animation();
        animation->manager = FbxManager::Create();
        if (!animation->manager)
            throw std::runtime_error("Autodesk FBX SDK could not create its animation manager.");
        FbxIOSettings* settings = FbxIOSettings::Create(animation->manager, IOSROOT);
        animation->manager->SetIOSettings(settings);
        settings->SetBoolProp(IMP_FBX_MATERIAL, true);
        settings->SetBoolProp(IMP_FBX_TEXTURE, true);
        settings->SetBoolProp(IMP_FBX_LINK, true);
        settings->SetBoolProp(IMP_FBX_SHAPE, false);
        settings->SetBoolProp(IMP_FBX_ANIMATION, false);
        settings->SetBoolProp(IMP_FBX_EXTRACT_EMBEDDED_DATA, true);

        std::unique_ptr<FbxImporter, void(*)(FbxImporter*)> importer(
            FbxImporter::Create(animation->manager, ""),
            [](FbxImporter* value) { if (value) value->Destroy(); });
        if (!importer->Initialize(source_path.c_str(), -1, settings))
            throw std::runtime_error(importer->GetStatus().GetErrorString());

        animation->extracted_media_directory = create_media_directory();
        if (!animation->extracted_media_directory.empty()) {
            const std::string media = animation->extracted_media_directory.u8string();
            importer->SetEmbeddingExtractionFolder(media.c_str());
        }
        animation->scene = FbxScene::Create(animation->manager, "PhysicsToolAnimatedModel");
        if (!animation->scene || !importer->Import(animation->scene))
            throw std::runtime_error(importer->GetStatus().GetErrorString());

        animation->frames_per_second = animation_frames_per_second(time_unit);
        if (has_animation && !create_animation_curves(
                animation->scene,
                channels,
                linear_unit,
                animation->frames_per_second,
                animation_error))
            throw std::runtime_error(animation_error);

        FbxAxisSystem::OpenGL.ConvertScene(animation->scene);
        FbxSystemUnit::m.ConvertScene(animation->scene);
        FbxGeometryConverter converter(animation->manager);
        if (!converter.Triangulate(animation->scene, true, false))
            throw std::runtime_error("Autodesk FBX SDK could not triangulate every animated mesh.");

        collect_bone_nodes(animation->bone_nodes, animation->scene->GetRootNode());
        if (has_animation) {
            std::unordered_set<std::string> seen_animated_bones;
            for (const auto& channel : channels) {
                if (channel.keys.empty() ||
                    (channel.attribute.rfind("translate", 0) != 0 &&
                     channel.attribute.rfind("rotate", 0) != 0 &&
                     channel.attribute.rfind("scale", 0) != 0)) continue;
                const std::string name = normalized_node_name(channel.node);
                if (!name.empty() && seen_animated_bones.insert(name).second)
                    animation->animated_bone_names.push_back(name);
            }
        }
        animation->start_frame = start_frame;
        animation->end_frame = std::max(start_frame, end_frame);
        return animation;
    } catch (const std::exception& exception) {
        release_animation(animation);
        set_error(errors, capacity, exception.what());
        return nullptr;
    } catch (...) {
        release_animation(animation);
        set_error(errors, capacity, "Unknown Autodesk FBX SDK animation import error.");
        return nullptr;
    }
}

PT_EXPORT pt_fbx_animation* pt_fbx_pose_open(
    const wchar_t* path,
    char* errors,
    std::size_t capacity)
{
    return pt_fbx_animation_open(path, nullptr, errors, capacity);
}

static pt_fbx_scene* sample_animation_impl(
    pt_fbx_animation* animation,
    double frame,
    const int* bone_indices,
    const float* bone_transforms,
    std::size_t override_count,
    bool bones_only,
    bool dynamic_only,
    char* errors,
    std::size_t capacity)
{
    try {
        if (!animation || !animation->scene) {
            set_error(errors, capacity, "No visual animation is loaded.");
            return nullptr;
        }
        FbxTime time;
        time.SetSecondDouble(frame / animation->frames_per_second);
        pose_context pose{ time };
        for (std::size_t index = 0; index < override_count; ++index) {
            const int bone_index = bone_indices ? bone_indices[index] : -1;
            if (bone_index < 0 ||
                static_cast<std::size_t>(bone_index) >= animation->bone_nodes.size() ||
                !bone_transforms) continue;
            const float* values = bone_transforms + index * 10;
            FbxAMatrix transform;
            transform.SetIdentity();
            transform.SetT(FbxVector4(values[0], values[1], values[2]));
            transform.SetQ(FbxQuaternion(values[3], values[4], values[5], values[6]));
            transform.SetS(FbxVector4(values[7], values[8], values[9]));
            pose.overrides[animation->bone_nodes[bone_index]] = transform;
        }

        auto result = std::make_unique<pt_fbx_scene>();
        result->extracted_media_directory = animation->extracted_media_directory;
        result->owns_extracted_media = false;
        pose_context* active_pose = pose.overrides.empty() ? nullptr : &pose;
        append_bones(*result, animation->scene->GetRootNode(), -1, time, active_pose);
        if (bones_only)
            return result.release();

        visit(*result, animation->scene->GetRootNode(), true, time, active_pose, dynamic_only);
        result->meshes.erase(std::remove_if(result->meshes.begin(), result->meshes.end(),
            [dynamic_only](const pt_fbx_mesh& mesh) {
                return dynamic_only ? mesh.vertices.empty() : mesh.indices.empty();
            }), result->meshes.end());
        if (result->meshes.empty()) {
            set_error(errors, capacity, "The animated FBX contained no displayable triangle meshes.");
            return nullptr;
        }
        return result.release();
    } catch (const std::exception& exception) {
        set_error(errors, capacity, exception.what());
        return nullptr;
    } catch (...) {
        set_error(errors, capacity, "Unknown Autodesk FBX SDK animation sampling error.");
        return nullptr;
    }
}

PT_EXPORT pt_fbx_scene* pt_fbx_animation_sample(
    pt_fbx_animation* animation,
    double frame,
    char* errors,
    std::size_t capacity)
{
    return sample_animation_impl(
        animation, frame, nullptr, nullptr, 0, false, false, errors, capacity);
}

PT_EXPORT pt_fbx_scene* pt_fbx_animation_sample_bones(
    pt_fbx_animation* animation,
    double frame,
    char* errors,
    std::size_t capacity)
{
    return sample_animation_impl(
        animation, frame, nullptr, nullptr, 0, true, false, errors, capacity);
}

PT_EXPORT pt_fbx_scene* pt_fbx_animation_sample_overrides(
    pt_fbx_animation* animation,
    double frame,
    const int* bone_indices,
    const float* bone_transforms,
    std::size_t override_count,
    char* errors,
    std::size_t capacity)
{
    return sample_animation_impl(
        animation,
        frame,
        bone_indices,
        bone_transforms,
        override_count,
        false,
        false,
        errors,
        capacity);
}

PT_EXPORT pt_fbx_scene* pt_fbx_animation_sample_pose(
    pt_fbx_animation* animation,
    double frame,
    char* errors,
    std::size_t capacity)
{
    return sample_animation_impl(
        animation, frame, nullptr, nullptr, 0, false, true, errors, capacity);
}

PT_EXPORT pt_fbx_scene* pt_fbx_animation_sample_pose_overrides(
    pt_fbx_animation* animation,
    double frame,
    const int* bone_indices,
    const float* bone_transforms,
    std::size_t override_count,
    char* errors,
    std::size_t capacity)
{
    return sample_animation_impl(
        animation,
        frame,
        bone_indices,
        bone_transforms,
        override_count,
        false,
        true,
        errors,
        capacity);
}

PT_EXPORT double pt_fbx_animation_start_frame(const pt_fbx_animation* animation)
{ return animation ? animation->start_frame : 0.0; }

PT_EXPORT double pt_fbx_animation_end_frame(const pt_fbx_animation* animation)
{ return animation ? animation->end_frame : 0.0; }

PT_EXPORT double pt_fbx_animation_frames_per_second(const pt_fbx_animation* animation)
{ return animation ? animation->frames_per_second : 30.0; }

PT_EXPORT std::size_t pt_fbx_animation_track_bone_count(const pt_fbx_animation* animation)
{ return animation ? animation->animated_bone_names.size() : 0; }

PT_EXPORT const char* pt_fbx_animation_track_bone_name(
    const pt_fbx_animation* animation,
    std::size_t index)
{
    return animation && index < animation->animated_bone_names.size()
        ? animation->animated_bone_names[index].c_str()
        : nullptr;
}

PT_EXPORT void pt_fbx_animation_free(pt_fbx_animation* animation)
{ release_animation(animation); }
PT_EXPORT pt_fbx_scene* pt_fbx_load(
    const wchar_t* path, char* errors, std::size_t capacity)
{
    return load_fbx_impl(path, nullptr, 0.0, errors, capacity);
}

PT_EXPORT pt_fbx_scene* pt_fbx_load_animated(
    const wchar_t* path,
    const wchar_t* animation_path,
    double frame,
    char* errors,
    std::size_t capacity)
{
    return load_fbx_impl(path, animation_path, frame, errors, capacity);
}
PT_EXPORT void pt_fbx_free(pt_fbx_scene* scene)
{
    if (!scene) return;
    std::filesystem::path media = scene->extracted_media_directory;
    const bool remove_media = scene->owns_extracted_media;
    delete scene;
    std::error_code ignored;
    if (remove_media && !media.empty()) std::filesystem::remove_all(media, ignored);
}
PT_EXPORT std::size_t pt_fbx_mesh_count(const pt_fbx_scene* scene)
{ return scene ? scene->meshes.size() : 0; }
PT_EXPORT const char* pt_fbx_mesh_name(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->meshes.size() ? scene->meshes[index].name.c_str() : nullptr; }
PT_EXPORT const char* pt_fbx_mesh_texture_path(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->meshes.size() ? scene->meshes[index].texture_path.c_str() : nullptr; }
PT_EXPORT const char* pt_fbx_mesh_specular_texture_path(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->meshes.size() ? scene->meshes[index].specular_texture_path.c_str() : nullptr; }
PT_EXPORT const char* pt_fbx_mesh_normal_texture_path(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->meshes.size() ? scene->meshes[index].normal_texture_path.c_str() : nullptr; }
PT_EXPORT const char* pt_fbx_mesh_ambient_texture_path(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->meshes.size() ? scene->meshes[index].ambient_texture_path.c_str() : nullptr; }
PT_EXPORT const char* pt_fbx_mesh_texture_uv_set(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->meshes.size() ? scene->meshes[index].texture_uv_set.c_str() : nullptr; }
PT_EXPORT const char* pt_fbx_mesh_specular_uv_set(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->meshes.size() ? scene->meshes[index].specular_uv_set.c_str() : nullptr; }
PT_EXPORT const char* pt_fbx_mesh_normal_uv_set(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->meshes.size() ? scene->meshes[index].normal_uv_set.c_str() : nullptr; }
PT_EXPORT const char* pt_fbx_mesh_ambient_uv_set(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->meshes.size() ? scene->meshes[index].ambient_uv_set.c_str() : nullptr; }

PT_EXPORT std::size_t pt_fbx_mesh_uv_set_count(const pt_fbx_scene* scene, std::size_t index)
{
    return scene && index < scene->meshes.size() ? scene->meshes[index].uv_sets.size() : 0;
}
PT_EXPORT const char* pt_fbx_mesh_uv_set_name(
    const pt_fbx_scene* scene, std::size_t mesh_index, std::size_t uv_index)
{
    return scene && mesh_index < scene->meshes.size() &&
        uv_index < scene->meshes[mesh_index].uv_sets.size()
        ? scene->meshes[mesh_index].uv_sets[uv_index].name.c_str()
        : nullptr;
}
PT_EXPORT const float* pt_fbx_mesh_uv_set_coordinates(
    const pt_fbx_scene* scene, std::size_t mesh_index, std::size_t uv_index, std::size_t* count)
{
    if (count) *count = 0;
    if (!scene || mesh_index >= scene->meshes.size() ||
        uv_index >= scene->meshes[mesh_index].uv_sets.size()) return nullptr;
    const auto& uv_set = scene->meshes[mesh_index].uv_sets[uv_index];
    if (count) *count = uv_set.coordinates.size() / 2;
    return uv_set.coordinates.empty() ? nullptr : uv_set.coordinates.data();
}

PT_EXPORT const std::uint8_t* pt_fbx_mesh_texture_data(
    const pt_fbx_scene* scene, std::size_t index, std::size_t* size)
{
    if (size) *size = 0;
    if (!scene || index >= scene->meshes.size()) return nullptr;
    if (size) *size = scene->meshes[index].texture_data.size();
    return scene->meshes[index].texture_data.empty() ? nullptr : scene->meshes[index].texture_data.data();
}
PT_EXPORT const float* pt_fbx_mesh_vertices(
    const pt_fbx_scene* scene, std::size_t index, std::size_t* count)
{
    if (count) *count = 0;
    if (!scene || index >= scene->meshes.size()) return nullptr;
    if (count) *count = scene->meshes[index].vertices.size() / vertex_stride;
    return scene->meshes[index].vertices.data();
}
PT_EXPORT const std::uint32_t* pt_fbx_mesh_indices(
    const pt_fbx_scene* scene, std::size_t index, std::size_t* count)
{
    if (count) *count = 0;
    if (!scene || index >= scene->meshes.size()) return nullptr;
    if (count) *count = scene->meshes[index].indices.size();
    return scene->meshes[index].indices.data();
}
PT_EXPORT int pt_fbx_mesh_culling_mode(const pt_fbx_scene* scene, std::size_t index)
{
    return scene && index < scene->meshes.size()
        ? scene->meshes[index].culling_mode
        : static_cast<int>(FbxNode::eCullingOff);
}
PT_EXPORT void pt_fbx_mesh_base_color(
    const pt_fbx_scene* scene, std::size_t index, float* rgba)
{
    if (!rgba) return;
    std::fill(rgba, rgba + 4, 1.0f);
    if (scene && index < scene->meshes.size())
        std::copy(scene->meshes[index].base_color.begin(), scene->meshes[index].base_color.end(), rgba);
}
PT_EXPORT std::size_t pt_fbx_bone_count(const pt_fbx_scene* scene)
{ return scene ? scene->bones.size() : 0; }

PT_EXPORT const char* pt_fbx_bone_name(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->bones.size() ? scene->bones[index].name.c_str() : nullptr; }

PT_EXPORT int pt_fbx_bone_parent(const pt_fbx_scene* scene, std::size_t index)
{ return scene && index < scene->bones.size() ? scene->bones[index].parent_index : -1; }

PT_EXPORT void pt_fbx_bone_transform(
    const pt_fbx_scene* scene,
    std::size_t index,
    float* position,
    float* rotation,
    float* scale)
{
    if (!scene || index >= scene->bones.size()) return;
    const auto& bone = scene->bones[index];
    if (position) std::copy(bone.position.begin(), bone.position.end(), position);
    if (rotation) std::copy(bone.rotation.begin(), bone.rotation.end(), rotation);
    if (scale) std::copy(bone.scale.begin(), bone.scale.end(), scale);
}
