/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BoundingBox = require('../../math/bounding-box');
const {getDomainFromParts} = require('../../math/domain');
const {buildTree} = require('./builder');

function createBvTreeFromVertices(vertices, indices)
{
    if(!vertices || vertices.length == 0 || !indices || indices.length == 0) {
        throw "Error: BvTree creation needs at least one polygon";
    }

    const polygons = indices.map(pair => pair.map(i => vertices[i]));

    let entries = polygons.map((p, i) => {
        return {
            index: i,
            domain: getDomainFromParts([{vertices: p}])
        };
    });

    return buildTree(entries);
};

function createBvTreeFromEntries(entries)
{
    if(!entries || entries.length == 0) {
        throw "Error: BvTree creation needs at least one child class";
    }

    let treeEntries = entries.map((entry, i) => {
        let domain = BoundingBox.fromTree(entry).getMinMax();
        return {index: i, domain: {
            min: domain.min.vec,
            max: domain.max.vec,
        }};
    });

    return buildTree(treeEntries);
};

/**
 * Algorithm for nested domains:
 * let m and M be the min and Max value of the current domain.
 * let m' and M' be the new sub-domain
 * let s be the vector from m to M (aka domain size).
 * let a and b be the values from data with 4-bits each of an 8-bit value
 *
 * a = sqrt(+226 * (m' - m) / s)
 * b = sqrt(-226 * (M' - M) / s)
 */
function createSubDomain(domain, subDomain)
{
    if (!domain) {
        return [0, 0, 0];
    }

    const data = new Array(3);
    for(let i=0; i<3; ++i)
    {
        const sNorm = 226.0 / (domain.max[i] - domain.min[i]);
        let a = Math.sqrt((subDomain.min[i] - domain.min[i]) *  sNorm) || 0;
        let b = Math.sqrt((subDomain.max[i] - domain.max[i]) * -sNorm) || 0;

        a = Math.min(0xF, Math.floor(a));
        b = Math.min(0xF, Math.floor(b));

        data[i] = (a << 4) | b;
    }
    return data;
}

module.exports = {createBvTreeFromVertices, createBvTreeFromEntries, createSubDomain};