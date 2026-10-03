/**
* @copyright 2018 - Max Bebök
* @author Max Bebök
* @license GNU-GPLv3 - see the "LICENSE" file in the root directory
*/

module.exports = class Vector3
{
    constructor(vec)
    {
        if(typeof(vec) == "number") {
            vec = [vec, vec, vec];
        }
        this.vec = (vec || [0.0, 0.0, 0.0]).slice(0, 3);
    }

    length()
    {
        return Math.sqrt(this.vec.reduce((sum, v) => sum + v*v, 0));
    }

    normalize()
    {
        const len = this.length();
        if(len == 0.0) {
            return new Vector3();
        }
        return new Vector3(this.vec.map(v => v / len));
    }

    subtract({vec})
    {
        return new Vector3(
            this.vec.map((val, i) => val - vec[i])
        );
    }

    add({vec})
    {
        return new Vector3(
            this.vec.map((val, i) => val + vec[i])
        );
    }

    multiply({vec})
    {
        return new Vector3(
            this.vec.map((val, i) => val * vec[i])
        );
    }

    cross({vec})
    {
        return new Vector3([
            this.vec[1] * vec[2] - this.vec[2] * vec[1],
            this.vec[2] * vec[0] - this.vec[0] * vec[2],
            this.vec[0] * vec[1] - this.vec[1] * vec[0],
        ]);
    }

    negate()
    {
        return new Vector3(this.vec.map(v => -v));
    }

    scale(scalar)
    {
        return new Vector3(this.vec.map(v => v * scalar));
    }

    dot({vec})
    {
        return this.vec.map((val, i) => val * vec[i])
             .reduce((sum, v) => sum + v, 0);
    }

    copy()
    {
        return new Vector3([...this.vec]);
    }

    static min(vecA, vecB)
    {
        return new Vector3([
            Math.min(vecA.vec[0], vecB.vec[0]),
            Math.min(vecA.vec[1], vecB.vec[1]),
            Math.min(vecA.vec[2], vecB.vec[2]),
        ]);
    }

    static max(vecA, vecB)
    {
        return new Vector3([
            Math.max(vecA.vec[0], vecB.vec[0]),
            Math.max(vecA.vec[1], vecB.vec[1]),
            Math.max(vecA.vec[2], vecB.vec[2]),
        ]);
    }
}

/**
function readMantissa(bin)
{
    bin = bin.trimEnd();
    return 1.0 + parseInt(bin, 2) / Math.pow(2, bin.length);
}
exp = Math.pow(2, exp - 127);
const floats = mantissa.map((m, i) => bits[i] * readMantissa(m) * exp);
*/