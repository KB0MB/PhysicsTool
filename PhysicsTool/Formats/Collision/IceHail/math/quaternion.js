/**
* @copyright 2019 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" this.file in the root directory
*/

const degToRad = deg => deg * (Math.PI / 180);

module.exports = class Quaternion
{
    static fromEuler(rotX, rotY, rotZ)
    {
        rotX = degToRad(rotX);
        rotY = degToRad(rotY);
        rotZ = degToRad(rotZ);

        const cosX = Math.cos(rotX * 0.5);
        const sinX = Math.sin(rotX * 0.5);

        const cosY = Math.cos(rotY * 0.5);
        const sinY = Math.sin(rotY * 0.5);

        const cosZ = Math.cos(rotZ * 0.5);
        const sinZ = Math.sin(rotZ * 0.5);

        return [
            (cosX * cosY * cosZ) + (sinX * sinY * sinZ),
            (cosX * cosY * sinZ) - (sinX * sinY * cosZ),
            (sinX * cosY * sinZ) + (cosX * sinY * cosZ),
            (sinX * cosY * cosZ) - (cosX * sinY * sinZ),
        ];
    }
};