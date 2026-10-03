/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

function createChunk(classMap)
{
    const chunk = {
        type: classMap.className,
        ...classMap.data
    };

    if(classMap.children.length > 0) {
        chunk.children = classMap.children.map(child => createChunk(child));
    }

    return chunk;
}

module.exports = function fileToJson(classMap)
{
    return {
        version: 4,
        data: createChunk(classMap)
    };
};