// gsys_assign_dynamic2: object id. RT1 = the two user-data words split into 16-bit halves (normalized)
layout (location = 1) out vec4 oObjectId;

void main()
{
    uint a = floatBitsToUint(Mat.p_user_data1), b = floatBitsToUint(Mat.p_user_data);
    oObjectId = vec4(float(a >> 16), float(a & 0xFFFFu), float(b >> 16), float(b & 0xFFFFu)) * 1.52590219e-05;
}
