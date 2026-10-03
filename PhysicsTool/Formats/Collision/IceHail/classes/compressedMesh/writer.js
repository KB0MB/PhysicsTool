/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" file in the root directory
*/

const {writeCounter, writeVector4} = require('../../hkrbHelper');
const {writeMaterials} = require('./material');
const {writeIndices} = require('./indices');
const {createDataRuns, writeDataRuns} = require('./dataRun');
const {writeVertices, getVertexScale} = require('./vertices');
const {calcDomainW, mergeDomains} = require('../../math/domain');
const {createBvTreeFromVertices} = require('../bvTree/creator');
const {writeBvTree} = require('./bvTreeWriter');
const materialIdMap = require('../../materialList');

/**
 * Header section, seems to be the same for all files.
 * Don't know what any of this is.
 */
function _writeHeader(file)
{
    file.skip(8);
    file.write('u32', 0x00040000);
    file.write('u32', 0);

    file.write('u16', 0x0300);
    file.write('u16', 0);
    file.write('u32', [0, 0]);
    file.write('u32', 0x00010100);
}

function _getBitLength(x)
{
    return x == 0 ? 0 : x.toString(2).length;
}

module.exports = (file, collData, shape) =>
{
    // @TODO also read and handle these guys from json files
    const sharedVertices = [];
    const sharedVertexIndices = [];

    const materialInfo = collData.materials.map((matName) => materialIdMap[matName] || materialIdMap.default); // dungeon stone, non climbable

    const countIndices  = collData.parts.reduce((sum, entry) => sum + entry.indices.length, 0);
    const countVertices = collData.parts.reduce((sum, entry) => sum + entry.vertices.length, 0);

    // unknown counter / constants
    const counter0 = 1;
    const counter3 = 1;

    _writeHeader(file);

    const ptrData0 = shape.writeCounterPointer(counter0); // counter0
    const ptrMaterialInfo = shape.writeCounterPointer(materialInfo.length); // counter1

    const ptrMaterials = shape.writeCounterPointer(collData.materials.length); // material count
    file.alignTo(16);

    const ptrData2 = shape.writeCounterPointer(counter3); // counter3
    file.alignTo(16);

    const posObjectDomain = file.pos();
    writeVector4(file);
    writeVector4(file);

    const posKeyInfo = file.pos();
    file.write('u32', [0xFF, 0xFF, 0xFF]);

    const ptrParts = shape.writeCounterPointer(collData.parts.length);
    const ptrIndices = shape.writeCounterPointer(countIndices);
    const ptrSharedVertexIndices = shape.writeCounterPointer(sharedVertices.length);
    const ptrVertices = shape.writeCounterPointer(countVertices);
    const ptrSharedVertices = shape.writeCounterPointer(sharedVertexIndices.length);
    const ptrDataRuns = shape.writeCounterPointer(countIndices); // @TODO works for now, use from dataRuns directly later

    file.alignTo(16);

    ptrData0.add();
    for(let i=0; i<counter0; ++i) {
        //file.write('u32', 0x84000000); // dungeon, no re-spawn
        file.write('u32', 0x90000000); // re-spawns
    }
    file.alignTo(16);

    ptrMaterialInfo.add();
    file.write('u32', materialInfo);
    file.alignTo(16);

    ptrMaterials.add();
    writeMaterials(file, collData.materials, ptrMaterials);
    file.alignTo(16);

    ptrData2.add();
    for(let i=0; i<counter3; ++i) {
        file.write('u32', 0); // unknown, different between files
    }
    file.alignTo(16);

    // @TODO shared vertices/indices
    let sharedIndexStart = 0;
    let sharedVerticesCount = 0;
    let sharedIndexCount = 0;

    let vertexArrayOffset = 0;
    let indexArrayOffset = 0;
    let dataRunArrayOffset = 0;

    let primitiveKeyCount = 0;
    let maxKeyVal = 0;

    const ptrPartsArray = ptrParts.add();
    const ptrPartEntries = [];
    const posTreeSize = [];

    let objectDomain = undefined;

    for(let part of collData.parts)
    {
        part.tree = createBvTreeFromVertices(part.vertices, part.indices);
        objectDomain = objectDomain ? mergeDomains(objectDomain, part.tree.domain) : part.tree.domain;

        part.domain = calcDomainW(part.tree.domain);
        part.offset = part.domain.min.slice(0, 3);
        part.scale = getVertexScale(part.domain);
        part.dataRuns = createDataRuns(part.indices);

        ptrPartEntries.push(ptrPartsArray.add());
        posTreeSize.push(file.pos());
        file.write('u32', [0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF]); // counter is written later
        file.alignTo(16);

        writeVector4(file, part.domain.min);
        writeVector4(file, part.domain.max);

        file.write("float32", part.offset);
        file.write("float32", part.scale);

        file.write('u32', vertexArrayOffset);

        file.write('u8', 0);
        file.write('u16', sharedIndexStart);
        file.write('u8', part.vertices.length); // not sure

        file.write('u8', 0);
        file.write('u16', indexArrayOffset);
        file.write('u8', part.indices.length);

        file.write('u8', 0);
        file.write('u16', dataRunArrayOffset);
        file.write('u8', part.dataRuns.length);

        file.write('u8', part.vertices.length);
        file.write('u8', sharedIndexCount); // sharedIndexCount

        file.write('u8', 0x00); // leafIndex?
        file.write('u8', 0x00); // page?
        file.write('u8', 0x00); // flags?
        file.write('u8', 0x00); // layeredData?

        file.alignTo(16);

        vertexArrayOffset += part.vertices.length;
        indexArrayOffset += part.indices.length;
        dataRunArrayOffset += part.dataRuns.length;
        primitiveKeyCount += part.indices.map(i => i.length == 4 ? 2 : 1)
            .reduce((sum, x) => sum + x, 0);
    }

    collData.parts.forEach((part, i) =>
    {
        ptrPartEntries[i].add();

        const treeElementCount = writeBvTree(file, part.tree);
        maxKeyVal = treeElementCount - 1;

        file.posPush();
        file.pos(posTreeSize[i]);
        writeCounter(file, treeElementCount);
        file.posPop();

        file.alignTo(16);
    });

    ptrIndices.add();
    collData.parts.forEach(part => writeIndices(file, part.indices));
    file.alignTo(16);

    //ptrSharedVertexIndices.add();
    // @TODO write shared vertex indices
    //file.alignTo(16);

    ptrVertices.add();
    collData.parts.forEach(part => writeVertices(file,  part.vertices, part.offset, part.scale));
    file.alignTo(16);

    //ptrSharedVertices.add();
    // @TODO write shared vertices
    //file.alignTo(16);

    ptrDataRuns.add();
    collData.parts.forEach(part => writeDataRuns(file,  part.dataRuns));
    file.alignTo(16);

    const bitsPerKey = _getBitLength(maxKeyVal) + _getBitLength(collData.parts.length - 1);
    // write global key info
    file.write('u32', primitiveKeyCount, posKeyInfo);
    file.write('u32', bitsPerKey, posKeyInfo + 4);
    file.write('u32', maxKeyVal, posKeyInfo + 8);

    // write global object domain
    file.posPush();

    file.pos(posObjectDomain);
    objectDomain = calcDomainW(objectDomain);
    writeVector4(file, objectDomain.min);
    writeVector4(file, objectDomain.max);

    file.posPop();
};

