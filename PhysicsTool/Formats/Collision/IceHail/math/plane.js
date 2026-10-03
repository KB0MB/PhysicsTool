/**
* @copyright 2018 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" file in the root directory
*/

const Vector3 = require("./vector3");

const POINT_DISTANCE_DELTA = 0.001;

module.exports = {
    pointsToPlane: (vecA, vecB, vecC) =>
    {
        const diffAB = vecB.subtract(vecA);
        const diffAC = vecC.subtract(vecA);
        const normVec = diffAB.cross(diffAC).normalize();

        const plane = [
            ...normVec.vec,
            -vecA.dot(normVec)
        ];

        if(plane[0] == 0.0 && plane[1] == 0.0 && plane[2] == 0.0) {
            throw "Point of a vertex can not be in a line";
        }

        return plane;
    },
    pointOnPlane: (plane, point) => {
        const n = new Vector3(plane);
        const nLength = n.length();
        const distance = (n.dot(point) + plane[3]) / nLength;

        return Math.abs(distance) < POINT_DISTANCE_DELTA;
    }
}