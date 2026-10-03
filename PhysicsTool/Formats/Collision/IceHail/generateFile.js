/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const HKRB = require("./files/hkrb");
const HKSC = require("./files/hksc");

module.exports = function generateFile(collData)
{
    const hksc = new HKSC();
    const hkrb = new HKRB();

    let rootFile = hkrb;

    if(collData.compound)
    {
        hksc.create(collData);
        hksc.setChild(hkrb);
        rootFile = hksc;
    }

    hkrb.create(collData);

    return rootFile.getBuffer();
}