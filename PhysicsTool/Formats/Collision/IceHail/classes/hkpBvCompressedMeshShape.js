/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BaseClass = require("./base");
const readCompressedMesh = require('./compressedMesh/reader');
const writeCompressedMesh = require('./compressedMesh/writer');

/**
 * Bv Compressed Mesh Shape
 * A compressed mesh with material information
 * @extends {BaseClass}
 */
module.exports = class hkpBvCompressedMeshShape extends BaseClass
{
    _create()
    {
        return writeCompressedMesh(this.file, this.collData, this);
    }

    read(chunk)
    {
        return readCompressedMesh(this.file, chunk);
    }
};