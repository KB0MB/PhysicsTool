/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const BinaryFile = require("./binaryFile");
const HKRB = require("./files/hkrb");
const HKSC = require("./files/hksc");

module.exports = function parseFile(hkData, extension)
{
    const hksc = new HKSC();
    const hkrb = new HKRB();

    let hkFile = new BinaryFile(hkData).setEndian("big").readOnly(true);
    let compoundData = undefined;

    if([".hksc"].includes(extension))
    {
        compoundData = hksc.read(hkFile);
        hkFile = hkFile.createSubFile(hkFile.pos());
    }

    const collData = hkrb.read(hkFile);
    if(compoundData) {
        collData.compound = compoundData.data;
    }

    return collData;
}