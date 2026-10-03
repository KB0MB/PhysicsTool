/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" file in the root directory
*/

const {readVector4, readCounter} = require("../../hkrbHelper");
const Vector3 = require("../../math/vector3");
const {readBvTree} = require('./bvTreeReader');
const {readVertices, readSharedVertices} = require('./vertices');
const {readDataRuns} = require('./dataRun');
const {readIndices} = require('./indices');
const {readMaterials} = require('./material');

module.exports = (file, chunk) =>
{
    file.skip(16 * 2);

    const offsetData0 = chunk.dataLinks.get(file.pos());
    const counterData0 = readCounter(file);

    const offsetMaterialInfo = chunk.dataLinks.get(file.pos());
    const counterMaterialInfo = readCounter(file);

    const offsetMaterials = chunk.dataLinks.get(file.pos());
    const counterMaterials = readCounter(file);
    file.alignTo(16);

    const offsetData2 = chunk.dataLinks.get(file.pos());
    const counterData2 = readCounter(file);
    file.alignTo(16);

    const entry = {
        domain: {
            min: readVector4(file),
            max: readVector4(file),
        }
    };

    const globalScalar = new Vector3(entry.domain.max)
        .subtract(new Vector3(entry.domain.min)).vec;

    const primitiveKeyCount = file.read('u32');
    const bitsPerKey = file.read('u32');
    const maxKeyVal = file.read('u32');

    const offsetParts = chunk.dataLinks.get(file.pos());
    const counterParts = readCounter(file);

    const offsetIndices = chunk.dataLinks.get(file.pos());
    const countIndices = readCounter(file);

    const offsetSharedVertexIndices = chunk.dataLinks.get(file.pos());
    const countSharedVertexIndices = readCounter(file);

    const offsetVertices = chunk.dataLinks.get(file.pos());
    const countVertices = readCounter(file);

    const offsetSharedVertices = chunk.dataLinks.get(file.pos());
    const countSharedVertices = readCounter(file);

    const offsetDataRuns = chunk.dataLinks.get(file.pos());
    const countDataRuns = readCounter(file);

    file.pos(offsetMaterials);
    entry.materials = readMaterials(file, counterMaterials);

    file.pos(offsetParts);

    let parts = new Array(counterParts).fill()
        .map(() => readPart(file, chunk));

    file.pos(offsetIndices);
    const globalIndices = readIndices(file, countIndices);

    file.pos(offsetVertices);
    const globalVertices = readVertices(file, countVertices);

    file.pos(offsetSharedVertices);
    const globalSharedVertices = readSharedVertices(file, countSharedVertices, entry.domain.min, globalScalar);

    file.pos(offsetSharedVertexIndices);
    const globalSharedVertexIndices = file.readArray('u16', countSharedVertexIndices);

    if(process.argv.includes("--read-all")) {
        file.pos(offsetDataRuns);
        entry.dataRuns = readDataRuns(file, countDataRuns);
    }

    entry.parts = parts.map(part =>
    {
        let vertices = globalVertices
            .slice(part.firstPackedVertex, part.firstPackedVertex + part.packedVerticesCount)
            .map(vertex => vertex.multiply(part.domain.scale).add(part.domain.offset).vec);

        let sharedVertices = globalSharedVertexIndices
            .slice(part.sharedIndexStart, part.sharedIndexStart + part.sharedIndexCount) // get the shared index array
            .map(i => globalSharedVertices[i]); // get the vertices with the matching index

        return {
            vertices: [...vertices, ...sharedVertices],
            indices: globalIndices.slice(part.indicesStart, part.indicesStart + part.indicesCount),
            tree: part.tree,
        };
    });

    return entry;
}

function readPart(file, chunk)
{
    const part = {};
    const offsetBvTree = chunk.dataLinks.get(file.pos());
    part.countBvTree = readCounter(file);
    file.alignTo(16);

    part.domain = {
        min: readVector4(file),
        max: readVector4(file),
        offset: new Vector3(file.readArray("float32", 3)),
        scale:  new Vector3(file.readArray("float32", 3)),
    };

    part.firstPackedVertex = file.read("u32");

    file.skip(1);
    part.sharedIndexStart = file.read("u16");
    part.sharedVerticesCount = file.read("u8");

    file.skip(1);
    part.indicesStart = file.read("u16");
    part.indicesCount = file.read("u8");

    file.skip(1);
    part.dataRunsStart = file.read("u16");
    part.dataRunsCount = file.read("u8");

    part.packedVerticesCount = file.read("u8");
    part.sharedIndexCount = file.read("u8");

    part.leafIndex = file.read("u8"); // unknown, from reflection
    part.page = file.read("u8"); // unknown, from reflection
    part.flags = file.read("u8") ; // unknown, from reflection
    part.layeredData = file.read("u8"); // unknown, from reflection
    file.alignTo(16);

    file.posPush();
    file.pos(offsetBvTree);
    part.tree = readBvTree(file, part.countBvTree, part.domain);
    file.posPop();

    return part;
}
