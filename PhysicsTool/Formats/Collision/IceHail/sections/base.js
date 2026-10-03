/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

module.exports = class BaseSection
{
    getSize()
    {
        return this.file.getSize();
    }

    getBuffer()
    {
        return this.file.getBuffer();
    }
};