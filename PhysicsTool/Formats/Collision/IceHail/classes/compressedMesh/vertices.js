/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const Vector3 = require("../../math/vector3");
//const {readSharedVertex} = require("../../../build/Release/module");

const MASK_BIT_21 = 0b111111111111111111111;
const MASK_BIT_22 = 0b1111111111111111111111;

function readVertices(file, count)
{
    return file.readArray("u32", count)
        .map(val => new Vector3([
            ((val >>  0) & 0b11111111111),
            ((val >> 11) & 0b11111111111),
            ((val >> 22) &  0b1111111111),
        ]));
}

function writeVertices(file, vertices, offset, scale)
{
    for(const vertex of vertices)
    {
        const vertexNorm = [
            Math.round((vertex[0] - offset[0]) / scale[0]),
            Math.round((vertex[1] - offset[1]) / scale[1]),
            Math.round((vertex[2] - offset[2]) / scale[2]),
        ];
        file.write('u32',
            ((vertexNorm[0] & 0b11111111111) <<  0) |
            ((vertexNorm[1] & 0b11111111111) << 11) |
            ((vertexNorm[2] &  0b1111111111) << 22)
        );
    }
}

function readSharedVertices(file, count, offset, scale)
{
    const res = new Array(count);
    for(let i=0; i<count; ++i)
    {
        //@TODO might not work for little-endian
        const valLo = file.read('u32');
        const valHi = file.read('u32');
        const valMid = (valLo << 11) | ((valHi >> 21) & 0b11111111111);

        res[i] = [
            (valHi         & MASK_BIT_21) / MASK_BIT_21 * scale[0] + offset[0],
            (valMid        & MASK_BIT_21) / MASK_BIT_21 * scale[1] + offset[1],
            ((valLo >> 10) & MASK_BIT_22) / MASK_BIT_22 * scale[2] + offset[2],
        ];
    }

    return res;
}

function writeSharedVertices(file, vertices)
{
    for(const vertex of vertices)
    {
            const intX = Math.round(vertex[0] * MASK_BIT_21) & MASK_BIT_21;
            const intY = Math.round(vertex[1] * MASK_BIT_21) & MASK_BIT_21;
            const intZ = Math.round(vertex[2] * MASK_BIT_22) & MASK_BIT_22;

        file.write('u32',
            (intZ << 10) | (intY >>> 11) & 0xFFFFFFFF
        );
        file.write('u32',
            (intX | (intY  << 21)) & 0xFFFFFFFF
        );
    }
}

function getVertexScale(domain)
{
    return [
        (domain.max[0] - domain.min[0]) / 0b11111111111,
        (domain.max[1] - domain.min[1]) / 0b11111111111,
        (domain.max[2] - domain.min[2]) /  0b1111111111,
    ];
}

/*
function _readSharedVertices(file, count, offset, scale)
{
    const res = readSharedVertex(file.buffer, file.pos(), count, offset, scale);
    file.skip(count * 8);
    return res;
}*/

module.exports = {
    readVertices, readSharedVertices,
    writeVertices, writeSharedVertices,
    getVertexScale
};