/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" file in the root directory
*/

function readMaterials(file, count)
{
    file.skip(count * 4);

    const res = new Array(count);
    for(let i=0; i<count; ++i) {
        res[i] = file.readString();
        file.alignTo(2);
    }
    return res;
}

function writeMaterials(file, materials, ptrMaterial)
{
    const ptrMaterialName = [];

    materials.forEach(() => {
        ptrMaterialName.push(ptrMaterial.add());
        file.write('u32', 0);
    });

    materials.forEach((name, i) => {
        ptrMaterialName[i].add();
        file.writeString(name);
        file.alignTo(2);
    });
}

module.exports = {readMaterials, writeMaterials};