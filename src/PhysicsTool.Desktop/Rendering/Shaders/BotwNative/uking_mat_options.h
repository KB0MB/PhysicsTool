
#ifndef UKING_MAT_OPTIONS_H
#define UKING_MAT_OPTIONS_H

#ifndef BLENDER_RENDER
#define BLENDER_RENDER 0
#endif

// ==================== STATIC OPTIONS (407)
// uking_enable_hide_normal_pass  [0, 1]
#ifndef uking_enable_hide_normal_pass
#define uking_enable_hide_normal_pass 0 //@ choices="0 1"
#endif
// uking_reverse_polygon_offset  [0, 1]
#ifndef uking_reverse_polygon_offset
#define uking_reverse_polygon_offset 0 //@ choices="0 1"
#endif
// uking_edit_expand_depthshadow_far  [0, 1]
#ifndef uking_edit_expand_depthshadow_far
#define uking_edit_expand_depthshadow_far 0 //@ choices="0 1"
#endif
// uking_enable_map_rendering  [0, 1]
#ifndef uking_enable_map_rendering
#define uking_enable_map_rendering 0 //@ choices="0 1"
#endif
// uking_map_layer_id  [-1, 0, 1, 2, 3, 4]
#ifndef uking_map_layer_id
#define uking_map_layer_id 0 //@ choices="-1 0 1 2 3 4"
#endif
// uking_sampler_use_array  [0, 1]
#ifndef uking_sampler_use_array
#define uking_sampler_use_array 0 //@ choices="0 1"
#endif
// uking_texture0_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture0_texcoord
#define uking_texture0_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_texture0_decode  [0, 1, 2]
#ifndef uking_texture0_decode
#define uking_texture0_decode 0 //@ choices="0 1 2"
#endif
// uking_enable_proctexture0  [0, 1]
#ifndef uking_enable_proctexture0
#define uking_enable_proctexture0 0 //@ choices="0 1"
#endif
// uking_enable_indirect0  [0, 1]
#ifndef uking_enable_indirect0
#define uking_enable_indirect0 0 //@ choices="0 1"
#endif
// uking_texture0_post  [0, 1]
#ifndef uking_texture0_post
#define uking_texture0_post 0 //@ choices="0 1"
#endif
// uking_proctexture0_type  [0, 1, 2, 10, 100]
#ifndef uking_proctexture0_type
#define uking_proctexture0_type 0 //@ choices="0 1 2 10 100"
#endif
// uking_proctexture0_param_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture0_param_A
#define uking_proctexture0_param_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_proctexture0_param_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture0_param_B
#define uking_proctexture0_param_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_indirect0_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_indirect0_texcoord
#define uking_indirect0_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_indirect0_world_scaling  [0, 1]
#ifndef uking_enable_indirect0_world_scaling
#define uking_enable_indirect0_world_scaling 0 //@ choices="0 1"
#endif
// uking_indirect0_signed_fetch  [0, 1]
#ifndef uking_indirect0_signed_fetch
#define uking_indirect0_signed_fetch 1 //@ choices="0 1"
#endif
// uking_indirect0_type  [0, 1]
#ifndef uking_indirect0_type
#define uking_indirect0_type 0 //@ choices="0 1"
#endif
// uking_texture_array_texture0  [-1, 0, 1]
#ifndef uking_texture_array_texture0
#define uking_texture_array_texture0 -1 //@ choices="-1 0 1"
#endif
// uking_texture1_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture1_texcoord
#define uking_texture1_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_texture1_decode  [0, 1, 2]
#ifndef uking_texture1_decode
#define uking_texture1_decode 0 //@ choices="0 1 2"
#endif
// uking_enable_proctexture1  [0, 1]
#ifndef uking_enable_proctexture1
#define uking_enable_proctexture1 0 //@ choices="0 1"
#endif
// uking_enable_indirect1  [0, 1]
#ifndef uking_enable_indirect1
#define uking_enable_indirect1 0 //@ choices="0 1"
#endif
// uking_texture1_post  [0, 1]
#ifndef uking_texture1_post
#define uking_texture1_post 0 //@ choices="0 1"
#endif
// uking_proctexture1_type  [0, 1, 2, 10, 100]
#ifndef uking_proctexture1_type
#define uking_proctexture1_type 0 //@ choices="0 1 2 10 100"
#endif
// uking_proctexture1_param_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture1_param_A
#define uking_proctexture1_param_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_proctexture1_param_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture1_param_B
#define uking_proctexture1_param_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_indirect1_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_indirect1_texcoord
#define uking_indirect1_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_indirect1_world_scaling  [0, 1]
#ifndef uking_enable_indirect1_world_scaling
#define uking_enable_indirect1_world_scaling 0 //@ choices="0 1"
#endif
// uking_indirect1_signed_fetch  [0, 1]
#ifndef uking_indirect1_signed_fetch
#define uking_indirect1_signed_fetch 1 //@ choices="0 1"
#endif
// uking_indirect1_type  [0, 1]
#ifndef uking_indirect1_type
#define uking_indirect1_type 0 //@ choices="0 1"
#endif
// uking_texture_array_texture1  [-1, 0, 1]
#ifndef uking_texture_array_texture1
#define uking_texture_array_texture1 -1 //@ choices="-1 0 1"
#endif
// uking_texture2_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture2_texcoord
#define uking_texture2_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_texture2_decode  [0, 1, 2]
#ifndef uking_texture2_decode
#define uking_texture2_decode 0 //@ choices="0 1 2"
#endif
// uking_enable_proctexture2  [0, 1]
#ifndef uking_enable_proctexture2
#define uking_enable_proctexture2 0 //@ choices="0 1"
#endif
// uking_enable_indirect2  [0, 1]
#ifndef uking_enable_indirect2
#define uking_enable_indirect2 0 //@ choices="0 1"
#endif
// uking_texture2_post  [0, 1]
#ifndef uking_texture2_post
#define uking_texture2_post 0 //@ choices="0 1"
#endif
// uking_proctexture2_type  [0, 1, 2, 10, 100]
#ifndef uking_proctexture2_type
#define uking_proctexture2_type 0 //@ choices="0 1 2 10 100"
#endif
// uking_proctexture2_param_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture2_param_A
#define uking_proctexture2_param_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_proctexture2_param_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture2_param_B
#define uking_proctexture2_param_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_indirect2_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_indirect2_texcoord
#define uking_indirect2_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_indirect2_world_scaling  [0, 1]
#ifndef uking_enable_indirect2_world_scaling
#define uking_enable_indirect2_world_scaling 0 //@ choices="0 1"
#endif
// uking_indirect2_signed_fetch  [0, 1]
#ifndef uking_indirect2_signed_fetch
#define uking_indirect2_signed_fetch 1 //@ choices="0 1"
#endif
// uking_indirect2_type  [0, 1]
#ifndef uking_indirect2_type
#define uking_indirect2_type 0 //@ choices="0 1"
#endif
// uking_texture_array_texture2  [-1, 0, 1]
#ifndef uking_texture_array_texture2
#define uking_texture_array_texture2 -1 //@ choices="-1 0 1"
#endif
// uking_texture3_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture3_texcoord
#define uking_texture3_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_texture3_decode  [0, 1, 2]
#ifndef uking_texture3_decode
#define uking_texture3_decode 0 //@ choices="0 1 2"
#endif
// uking_enable_proctexture3  [0, 1]
#ifndef uking_enable_proctexture3
#define uking_enable_proctexture3 0 //@ choices="0 1"
#endif
// uking_enable_indirect3  [0, 1]
#ifndef uking_enable_indirect3
#define uking_enable_indirect3 0 //@ choices="0 1"
#endif
// uking_texture3_post  [0, 1]
#ifndef uking_texture3_post
#define uking_texture3_post 0 //@ choices="0 1"
#endif
// uking_proctexture3_type  [0, 1, 2, 10, 100]
#ifndef uking_proctexture3_type
#define uking_proctexture3_type 0 //@ choices="0 1 2 10 100"
#endif
// uking_proctexture3_param_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture3_param_A
#define uking_proctexture3_param_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_proctexture3_param_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture3_param_B
#define uking_proctexture3_param_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_indirect3_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_indirect3_texcoord
#define uking_indirect3_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_indirect3_world_scaling  [0, 1]
#ifndef uking_enable_indirect3_world_scaling
#define uking_enable_indirect3_world_scaling 0 //@ choices="0 1"
#endif
// uking_indirect3_signed_fetch  [0, 1]
#ifndef uking_indirect3_signed_fetch
#define uking_indirect3_signed_fetch 1 //@ choices="0 1"
#endif
// uking_indirect3_type  [0, 1]
#ifndef uking_indirect3_type
#define uking_indirect3_type 0 //@ choices="0 1"
#endif
// uking_texture_array_texture3  [-1, 0, 1]
#ifndef uking_texture_array_texture3
#define uking_texture_array_texture3 -1 //@ choices="-1 0 1"
#endif
// uking_texture4_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture4_texcoord
#define uking_texture4_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_texture4_decode  [0, 1, 2]
#ifndef uking_texture4_decode
#define uking_texture4_decode 0 //@ choices="0 1 2"
#endif
// uking_enable_proctexture4  [0, 1]
#ifndef uking_enable_proctexture4
#define uking_enable_proctexture4 0 //@ choices="0 1"
#endif
// uking_enable_indirect4  [0, 1]
#ifndef uking_enable_indirect4
#define uking_enable_indirect4 0 //@ choices="0 1"
#endif
// uking_texture4_post  [0, 1]
#ifndef uking_texture4_post
#define uking_texture4_post 0 //@ choices="0 1"
#endif
// uking_proctexture4_type  [0, 1, 2, 10, 100]
#ifndef uking_proctexture4_type
#define uking_proctexture4_type 0 //@ choices="0 1 2 10 100"
#endif
// uking_proctexture4_param_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture4_param_A
#define uking_proctexture4_param_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_proctexture4_param_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture4_param_B
#define uking_proctexture4_param_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_indirect4_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_indirect4_texcoord
#define uking_indirect4_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_indirect4_world_scaling  [0, 1]
#ifndef uking_enable_indirect4_world_scaling
#define uking_enable_indirect4_world_scaling 0 //@ choices="0 1"
#endif
// uking_indirect4_signed_fetch  [0, 1]
#ifndef uking_indirect4_signed_fetch
#define uking_indirect4_signed_fetch 1 //@ choices="0 1"
#endif
// uking_indirect4_type  [0, 1]
#ifndef uking_indirect4_type
#define uking_indirect4_type 0 //@ choices="0 1"
#endif
// uking_texture_array_texture4  [-1, 0, 1]
#ifndef uking_texture_array_texture4
#define uking_texture_array_texture4 -1 //@ choices="-1 0 1"
#endif
// uking_texture5_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture5_texcoord
#define uking_texture5_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_texture5_decode  [0, 1, 2]
#ifndef uking_texture5_decode
#define uking_texture5_decode 0 //@ choices="0 1 2"
#endif
// uking_enable_proctexture5  [0, 1]
#ifndef uking_enable_proctexture5
#define uking_enable_proctexture5 0 //@ choices="0 1"
#endif
// uking_enable_indirect5  [0, 1]
#ifndef uking_enable_indirect5
#define uking_enable_indirect5 0 //@ choices="0 1"
#endif
// uking_texture5_post  [0, 1]
#ifndef uking_texture5_post
#define uking_texture5_post 0 //@ choices="0 1"
#endif
// uking_proctexture5_type  [0, 1, 2, 10, 100]
#ifndef uking_proctexture5_type
#define uking_proctexture5_type 0 //@ choices="0 1 2 10 100"
#endif
// uking_proctexture5_param_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture5_param_A
#define uking_proctexture5_param_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_proctexture5_param_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_proctexture5_param_B
#define uking_proctexture5_param_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_indirect5_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_indirect5_texcoord
#define uking_indirect5_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_indirect5_world_scaling  [0, 1]
#ifndef uking_enable_indirect5_world_scaling
#define uking_enable_indirect5_world_scaling 0 //@ choices="0 1"
#endif
// uking_indirect5_signed_fetch  [0, 1]
#ifndef uking_indirect5_signed_fetch
#define uking_indirect5_signed_fetch 1 //@ choices="0 1"
#endif
// uking_indirect5_type  [0, 1]
#ifndef uking_indirect5_type
#define uking_indirect5_type 0 //@ choices="0 1"
#endif
// uking_texture_array_texture5  [-1, 0, 1]
#ifndef uking_texture_array_texture5
#define uking_texture_array_texture5 -1 //@ choices="-1 0 1"
#endif
// uking_texture6_texture  [0, 1, 2, 3, 4, 5]
#ifndef uking_texture6_texture
#define uking_texture6_texture 0 //@ choices="0 1 2 3 4 5"
#endif
// uking_texture6_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture6_texcoord
#define uking_texture6_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_texture6_decode  [0, 1, 2]
#ifndef uking_texture6_decode
#define uking_texture6_decode 0 //@ choices="0 1 2"
#endif
// uking_texture7_texture  [0, 1, 2, 3, 4, 5]
#ifndef uking_texture7_texture
#define uking_texture7_texture 0 //@ choices="0 1 2 3 4 5"
#endif
// uking_texture7_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture7_texcoord
#define uking_texture7_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_texture7_decode  [0, 1, 2]
#ifndef uking_texture7_decode
#define uking_texture7_decode 0 //@ choices="0 1 2"
#endif
// gsys_enable_color_buffer  [0, 1]
#ifndef gsys_enable_color_buffer
#define gsys_enable_color_buffer 0 //@ choices="0 1"
#endif
// uking_texture_scene_color_post  [0, 1]
#ifndef uking_texture_scene_color_post
#define uking_texture_scene_color_post 0 //@ choices="0 1"
#endif
// uking_texture_scene_color0_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture_scene_color0_texcoord
#define uking_texture_scene_color0_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_scene_color0_fog  [0, 1]
#ifndef uking_enable_scene_color0_fog
#define uking_enable_scene_color0_fog 0 //@ choices="0 1"
#endif
// uking_enable_scene_color0_edge_stop  [0, 1]
#ifndef uking_enable_scene_color0_edge_stop
#define uking_enable_scene_color0_edge_stop 0 //@ choices="0 1"
#endif
// uking_enable_scene_color0_edge_stop_magic_N  [0, 1]
#ifndef uking_enable_scene_color0_edge_stop_magic_N
#define uking_enable_scene_color0_edge_stop_magic_N 1 //@ choices="0 1"
#endif
// uking_texture_scene_color1_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture_scene_color1_texcoord
#define uking_texture_scene_color1_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_scene_color1_fog  [0, 1]
#ifndef uking_enable_scene_color1_fog
#define uking_enable_scene_color1_fog 0 //@ choices="0 1"
#endif
// uking_enable_depth_buffer  [0, 1]
#ifndef uking_enable_depth_buffer
#define uking_enable_depth_buffer 0 //@ choices="0 1"
#endif
// uking_texture_scene_depth_post  [0, 1]
#ifndef uking_texture_scene_depth_post
#define uking_texture_scene_depth_post 0 //@ choices="0 1"
#endif
// uking_texture_scene_depth0_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture_scene_depth0_texcoord
#define uking_texture_scene_depth0_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_texture_scene_depth1_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_texture_scene_depth1_texcoord
#define uking_texture_scene_depth1_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_scene_color0_depthdiff1_effect  [0, 1]
#ifndef uking_enable_scene_color0_depthdiff1_effect
#define uking_enable_scene_color0_depthdiff1_effect 0 //@ choices="0 1"
#endif
// uking_depth_diff_scale_to_scene_color0  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_depth_diff_scale_to_scene_color0
#define uking_depth_diff_scale_to_scene_color0 1 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_enable_calc_color0  [0, 1]
#ifndef uking_enable_calc_color0
#define uking_enable_calc_color0 0 //@ choices="0 1"
#endif
// uking_color0_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color0_calc_type
#define uking_color0_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color0_clamp01  [0, 1]
#ifndef uking_color0_clamp01
#define uking_color0_clamp01 0 //@ choices="0 1"
#endif
// uking_color0_before_normal  [0, 1]
#ifndef uking_color0_before_normal
#define uking_color0_before_normal 0 //@ choices="0 1"
#endif
// uking_color0_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color0_A
#define uking_color0_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color0_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color0_A_channel
#define uking_color0_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color0_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color0_B
#define uking_color0_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color0_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color0_B_channel
#define uking_color0_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color0_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color0_C
#define uking_color0_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color0_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color0_C_channel
#define uking_color0_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color0_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color0_D
#define uking_color0_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color0_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color0_D_channel
#define uking_color0_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color1  [0, 1]
#ifndef uking_enable_calc_color1
#define uking_enable_calc_color1 0 //@ choices="0 1"
#endif
// uking_color1_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color1_calc_type
#define uking_color1_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color1_clamp01  [0, 1]
#ifndef uking_color1_clamp01
#define uking_color1_clamp01 0 //@ choices="0 1"
#endif
// uking_color1_before_normal  [0, 1]
#ifndef uking_color1_before_normal
#define uking_color1_before_normal 0 //@ choices="0 1"
#endif
// uking_color1_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color1_A
#define uking_color1_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color1_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color1_A_channel
#define uking_color1_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color1_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color1_B
#define uking_color1_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color1_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color1_B_channel
#define uking_color1_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color1_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color1_C
#define uking_color1_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color1_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color1_C_channel
#define uking_color1_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color1_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color1_D
#define uking_color1_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color1_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color1_D_channel
#define uking_color1_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color2  [0, 1]
#ifndef uking_enable_calc_color2
#define uking_enable_calc_color2 0 //@ choices="0 1"
#endif
// uking_color2_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color2_calc_type
#define uking_color2_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color2_clamp01  [0, 1]
#ifndef uking_color2_clamp01
#define uking_color2_clamp01 0 //@ choices="0 1"
#endif
// uking_color2_before_normal  [0, 1]
#ifndef uking_color2_before_normal
#define uking_color2_before_normal 0 //@ choices="0 1"
#endif
// uking_color2_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color2_A
#define uking_color2_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color2_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color2_A_channel
#define uking_color2_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color2_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color2_B
#define uking_color2_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color2_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color2_B_channel
#define uking_color2_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color2_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color2_C
#define uking_color2_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color2_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color2_C_channel
#define uking_color2_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color2_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color2_D
#define uking_color2_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color2_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color2_D_channel
#define uking_color2_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color3  [0, 1]
#ifndef uking_enable_calc_color3
#define uking_enable_calc_color3 0 //@ choices="0 1"
#endif
// uking_color3_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color3_calc_type
#define uking_color3_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color3_clamp01  [0, 1]
#ifndef uking_color3_clamp01
#define uking_color3_clamp01 0 //@ choices="0 1"
#endif
// uking_color3_before_normal  [0, 1]
#ifndef uking_color3_before_normal
#define uking_color3_before_normal 0 //@ choices="0 1"
#endif
// uking_color3_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color3_A
#define uking_color3_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color3_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color3_A_channel
#define uking_color3_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color3_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color3_B
#define uking_color3_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color3_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color3_B_channel
#define uking_color3_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color3_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color3_C
#define uking_color3_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color3_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color3_C_channel
#define uking_color3_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color3_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color3_D
#define uking_color3_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color3_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color3_D_channel
#define uking_color3_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color4  [0, 1]
#ifndef uking_enable_calc_color4
#define uking_enable_calc_color4 0 //@ choices="0 1"
#endif
// uking_color4_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color4_calc_type
#define uking_color4_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color4_clamp01  [0, 1]
#ifndef uking_color4_clamp01
#define uking_color4_clamp01 0 //@ choices="0 1"
#endif
// uking_color4_before_normal  [0, 1]
#ifndef uking_color4_before_normal
#define uking_color4_before_normal 0 //@ choices="0 1"
#endif
// uking_color4_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color4_A
#define uking_color4_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color4_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color4_A_channel
#define uking_color4_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color4_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color4_B
#define uking_color4_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color4_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color4_B_channel
#define uking_color4_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color4_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color4_C
#define uking_color4_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color4_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color4_C_channel
#define uking_color4_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color4_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color4_D
#define uking_color4_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color4_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color4_D_channel
#define uking_color4_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color5  [0, 1]
#ifndef uking_enable_calc_color5
#define uking_enable_calc_color5 0 //@ choices="0 1"
#endif
// uking_color5_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color5_calc_type
#define uking_color5_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color5_clamp01  [0, 1]
#ifndef uking_color5_clamp01
#define uking_color5_clamp01 0 //@ choices="0 1"
#endif
// uking_color5_before_normal  [0, 1]
#ifndef uking_color5_before_normal
#define uking_color5_before_normal 0 //@ choices="0 1"
#endif
// uking_color5_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color5_A
#define uking_color5_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color5_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color5_A_channel
#define uking_color5_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color5_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color5_B
#define uking_color5_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color5_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color5_B_channel
#define uking_color5_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color5_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color5_C
#define uking_color5_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color5_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color5_C_channel
#define uking_color5_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color5_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color5_D
#define uking_color5_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color5_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color5_D_channel
#define uking_color5_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color6  [0, 1]
#ifndef uking_enable_calc_color6
#define uking_enable_calc_color6 0 //@ choices="0 1"
#endif
// uking_color6_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color6_calc_type
#define uking_color6_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color6_clamp01  [0, 1]
#ifndef uking_color6_clamp01
#define uking_color6_clamp01 0 //@ choices="0 1"
#endif
// uking_color6_before_normal  [0, 1]
#ifndef uking_color6_before_normal
#define uking_color6_before_normal 0 //@ choices="0 1"
#endif
// uking_color6_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color6_A
#define uking_color6_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color6_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color6_A_channel
#define uking_color6_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color6_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color6_B
#define uking_color6_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color6_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color6_B_channel
#define uking_color6_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color6_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color6_C
#define uking_color6_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color6_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color6_C_channel
#define uking_color6_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color6_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color6_D
#define uking_color6_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color6_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color6_D_channel
#define uking_color6_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color7  [0, 1]
#ifndef uking_enable_calc_color7
#define uking_enable_calc_color7 0 //@ choices="0 1"
#endif
// uking_color7_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color7_calc_type
#define uking_color7_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color7_clamp01  [0, 1]
#ifndef uking_color7_clamp01
#define uking_color7_clamp01 0 //@ choices="0 1"
#endif
// uking_color7_before_normal  [0, 1]
#ifndef uking_color7_before_normal
#define uking_color7_before_normal 0 //@ choices="0 1"
#endif
// uking_color7_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color7_A
#define uking_color7_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color7_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color7_A_channel
#define uking_color7_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color7_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color7_B
#define uking_color7_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color7_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color7_B_channel
#define uking_color7_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color7_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color7_C
#define uking_color7_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color7_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color7_C_channel
#define uking_color7_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color7_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color7_D
#define uking_color7_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color7_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color7_D_channel
#define uking_color7_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color8  [0, 1]
#ifndef uking_enable_calc_color8
#define uking_enable_calc_color8 0 //@ choices="0 1"
#endif
// uking_color8_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color8_calc_type
#define uking_color8_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color8_clamp01  [0, 1]
#ifndef uking_color8_clamp01
#define uking_color8_clamp01 0 //@ choices="0 1"
#endif
// uking_color8_before_normal  [0, 1]
#ifndef uking_color8_before_normal
#define uking_color8_before_normal 0 //@ choices="0 1"
#endif
// uking_color8_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color8_A
#define uking_color8_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color8_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color8_A_channel
#define uking_color8_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color8_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color8_B
#define uking_color8_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color8_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color8_B_channel
#define uking_color8_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color8_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color8_C
#define uking_color8_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color8_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color8_C_channel
#define uking_color8_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color8_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color8_D
#define uking_color8_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color8_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color8_D_channel
#define uking_color8_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color9  [0, 1]
#ifndef uking_enable_calc_color9
#define uking_enable_calc_color9 0 //@ choices="0 1"
#endif
// uking_color9_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color9_calc_type
#define uking_color9_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color9_clamp01  [0, 1]
#ifndef uking_color9_clamp01
#define uking_color9_clamp01 0 //@ choices="0 1"
#endif
// uking_color9_before_normal  [0, 1]
#ifndef uking_color9_before_normal
#define uking_color9_before_normal 0 //@ choices="0 1"
#endif
// uking_color9_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color9_A
#define uking_color9_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color9_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color9_A_channel
#define uking_color9_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color9_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color9_B
#define uking_color9_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color9_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color9_B_channel
#define uking_color9_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color9_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color9_C
#define uking_color9_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color9_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color9_C_channel
#define uking_color9_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color9_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color9_D
#define uking_color9_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color9_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color9_D_channel
#define uking_color9_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color10  [0, 1]
#ifndef uking_enable_calc_color10
#define uking_enable_calc_color10 0 //@ choices="0 1"
#endif
// uking_color10_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color10_calc_type
#define uking_color10_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color10_clamp01  [0, 1]
#ifndef uking_color10_clamp01
#define uking_color10_clamp01 0 //@ choices="0 1"
#endif
// uking_color10_before_normal  [0, 1]
#ifndef uking_color10_before_normal
#define uking_color10_before_normal 0 //@ choices="0 1"
#endif
// uking_color10_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color10_A
#define uking_color10_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color10_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color10_A_channel
#define uking_color10_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color10_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color10_B
#define uking_color10_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color10_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color10_B_channel
#define uking_color10_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color10_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color10_C
#define uking_color10_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color10_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color10_C_channel
#define uking_color10_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color10_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color10_D
#define uking_color10_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color10_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color10_D_channel
#define uking_color10_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color11  [0, 1]
#ifndef uking_enable_calc_color11
#define uking_enable_calc_color11 0 //@ choices="0 1"
#endif
// uking_color11_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color11_calc_type
#define uking_color11_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color11_clamp01  [0, 1]
#ifndef uking_color11_clamp01
#define uking_color11_clamp01 0 //@ choices="0 1"
#endif
// uking_color11_before_normal  [0, 1]
#ifndef uking_color11_before_normal
#define uking_color11_before_normal 0 //@ choices="0 1"
#endif
// uking_color11_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color11_A
#define uking_color11_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color11_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color11_A_channel
#define uking_color11_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color11_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color11_B
#define uking_color11_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color11_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color11_B_channel
#define uking_color11_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color11_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color11_C
#define uking_color11_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color11_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color11_C_channel
#define uking_color11_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color11_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color11_D
#define uking_color11_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color11_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color11_D_channel
#define uking_color11_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color12  [0, 1]
#ifndef uking_enable_calc_color12
#define uking_enable_calc_color12 0 //@ choices="0 1"
#endif
// uking_color12_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color12_calc_type
#define uking_color12_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color12_clamp01  [0, 1]
#ifndef uking_color12_clamp01
#define uking_color12_clamp01 0 //@ choices="0 1"
#endif
// uking_color12_before_normal  [0, 1]
#ifndef uking_color12_before_normal
#define uking_color12_before_normal 0 //@ choices="0 1"
#endif
// uking_color12_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color12_A
#define uking_color12_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color12_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color12_A_channel
#define uking_color12_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color12_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color12_B
#define uking_color12_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color12_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color12_B_channel
#define uking_color12_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color12_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color12_C
#define uking_color12_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color12_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color12_C_channel
#define uking_color12_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color12_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color12_D
#define uking_color12_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color12_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color12_D_channel
#define uking_color12_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_calc_color13  [0, 1]
#ifndef uking_enable_calc_color13
#define uking_enable_calc_color13 0 //@ choices="0 1"
#endif
// uking_color13_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_color13_calc_type
#define uking_color13_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_color13_clamp01  [0, 1]
#ifndef uking_color13_clamp01
#define uking_color13_clamp01 0 //@ choices="0 1"
#endif
// uking_color13_before_normal  [0, 1]
#ifndef uking_color13_before_normal
#define uking_color13_before_normal 0 //@ choices="0 1"
#endif
// uking_color13_A  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color13_A
#define uking_color13_A 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color13_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color13_A_channel
#define uking_color13_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color13_B  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color13_B
#define uking_color13_B 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color13_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color13_B_channel
#define uking_color13_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color13_C  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color13_C
#define uking_color13_C 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color13_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color13_C_channel
#define uking_color13_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_color13_D  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_color13_D
#define uking_color13_D 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_color13_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_color13_D_channel
#define uking_color13_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_replace_albedo  [0, 1]
#ifndef uking_enable_replace_albedo
#define uking_enable_replace_albedo 0 //@ choices="0 1"
#endif
// uking_replace_albedo_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_replace_albedo_color
#define uking_replace_albedo_color 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_enable_emission_cubemap  [0, 1]
#ifndef uking_enable_emission_cubemap
#define uking_enable_emission_cubemap 0 //@ choices="0 1"
#endif
// uking_emission_color_cubemap  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_emission_color_cubemap
#define uking_emission_color_cubemap 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// gsys_gbuffer_after_fx  [0, 1]
#ifndef gsys_gbuffer_after_fx
#define gsys_gbuffer_after_fx 0 //@ choices="0 1"
#endif
// uking_enable_fresnel_cheat  [0, 1]
#ifndef uking_enable_fresnel_cheat
#define uking_enable_fresnel_cheat 1 //@ choices="0 1"
#endif
// uking_enable_output_object_attribute  [0, 1]
#ifndef uking_enable_output_object_attribute
#define uking_enable_output_object_attribute 1 //@ choices="0 1"
#endif
// uking_pass_object_attribute  [0, 1]
#ifndef uking_pass_object_attribute
#define uking_pass_object_attribute 0 //@ choices="0 1"
#endif
// uking_edit_disable_actor_id  [0, 1]
#ifndef uking_edit_disable_actor_id
#define uking_edit_disable_actor_id 0 //@ choices="0 1"
#endif
// uking_inner_material  [0, 1]
#ifndef uking_inner_material
#define uking_inner_material 0 //@ choices="0 1"
#endif
// gsys_deferred_shading_material  [0, 1]
#ifndef gsys_deferred_shading_material
#define gsys_deferred_shading_material 1 //@ choices="0 1"
#endif
// uking_vtx_calc_type  [0, 1, 2]
#ifndef uking_vtx_calc_type
#define uking_vtx_calc_type 0 //@ choices="0 1 2"
#endif
// uking_vtx_texture0  [0, 1]
#ifndef uking_vtx_texture0
#define uking_vtx_texture0 0 //@ choices="0 1"
#endif
// uking_vtx_texture0_texcoord  [0, 1, 2, 6, 7, 8]
#ifndef uking_vtx_texture0_texcoord
#define uking_vtx_texture0_texcoord 0 //@ choices="0 1 2 6 7 8"
#endif
// uking_modify_position_type  [0, 1, 2, 3]
#ifndef uking_modify_position_type
#define uking_modify_position_type 0 //@ choices="0 1 2 3"
#endif
// uking_vtx_position_modify_calc_type  [0, 1, 17, 10, 19, 5, 16, 18, 20, 2, 8, 6, 9, 11, 21, 4, 15, 12, 27, 7, 28, 29, 30, 13, 14, 31, 32, 22, 23, 24, 25, 26, 3]
#ifndef uking_vtx_position_modify_calc_type
#define uking_vtx_position_modify_calc_type 0 //@ choices="0 1 17 10 19 5 16 18 20 2 8 6 9 11 21 4 15 12 27 7 28 29 30 13 14 31 32 22 23 24 25 26 3"
#endif
// uking_vtx_position_modify_shadow_only  [0, 1]
#ifndef uking_vtx_position_modify_shadow_only
#define uking_vtx_position_modify_shadow_only 0 //@ choices="0 1"
#endif
// uking_vtx_position_modify_clamp  [0, 1]
#ifndef uking_vtx_position_modify_clamp
#define uking_vtx_position_modify_clamp 0 //@ choices="0 1"
#endif
// uking_vtx_position_modify_vector  [100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 0, 300, 400, 405, 401, 406, 402, 403, 404]
#ifndef uking_vtx_position_modify_vector
#define uking_vtx_position_modify_vector 100 //@ choices="100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 0 300 400 405 401 406 402 403 404"
#endif
// uking_vtx_position_modify_A  [100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 0, 300, 400, 405, 401, 406, 402, 403, 404]
#ifndef uking_vtx_position_modify_A
#define uking_vtx_position_modify_A 100 //@ choices="100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 0 300 400 405 401 406 402 403 404"
#endif
// uking_vtx_position_modify_A_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_vtx_position_modify_A_channel
#define uking_vtx_position_modify_A_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_vtx_position_modify_B  [100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 0, 300, 400, 405, 401, 406, 402, 403, 404]
#ifndef uking_vtx_position_modify_B
#define uking_vtx_position_modify_B 100 //@ choices="100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 0 300 400 405 401 406 402 403 404"
#endif
// uking_vtx_position_modify_B_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_vtx_position_modify_B_channel
#define uking_vtx_position_modify_B_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_vtx_position_modify_C  [100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 0, 300, 400, 405, 401, 406, 402, 403, 404]
#ifndef uking_vtx_position_modify_C
#define uking_vtx_position_modify_C 100 //@ choices="100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 0 300 400 405 401 406 402 403 404"
#endif
// uking_vtx_position_modify_C_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_vtx_position_modify_C_channel
#define uking_vtx_position_modify_C_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_vtx_position_modify_D  [100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 0, 300, 400, 405, 401, 406, 402, 403, 404]
#ifndef uking_vtx_position_modify_D
#define uking_vtx_position_modify_D 100 //@ choices="100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 0 300 400 405 401 406 402 403 404"
#endif
// uking_vtx_position_modify_D_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_vtx_position_modify_D_channel
#define uking_vtx_position_modify_D_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_modify_normal_type  [0, 1]
#ifndef uking_modify_normal_type
#define uking_modify_normal_type 0 //@ choices="0 1"
#endif
// uking_vtx_normal_modify_A  [100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 0, 300, 400, 405, 401, 406, 402, 403, 404]
#ifndef uking_vtx_normal_modify_A
#define uking_vtx_normal_modify_A 100 //@ choices="100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 0 300 400 405 401 406 402 403 404"
#endif
// uking_enable_wind_vtx_transform  [0, 1]
#ifndef uking_enable_wind_vtx_transform
#define uking_enable_wind_vtx_transform 0 //@ choices="0 1"
#endif
// uking_wind_vtx_transform_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_wind_vtx_transform_channel
#define uking_wind_vtx_transform_channel 10 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_wind_vtx_transform_coreinfo  [0, 1]
#ifndef uking_enable_wind_vtx_transform_coreinfo
#define uking_enable_wind_vtx_transform_coreinfo 0 //@ choices="0 1"
#endif
// uking_enable_wind_vtx_transform_normal_lock  [0, 1]
#ifndef uking_enable_wind_vtx_transform_normal_lock
#define uking_enable_wind_vtx_transform_normal_lock 0 //@ choices="0 1"
#endif
// uking_enable_wind_vtx_transform_lie  [0, 1]
#ifndef uking_enable_wind_vtx_transform_lie
#define uking_enable_wind_vtx_transform_lie 0 //@ choices="0 1"
#endif
// uking_enable_wind_vtx_transform_height  [0, 1]
#ifndef uking_enable_wind_vtx_transform_height
#define uking_enable_wind_vtx_transform_height 0 //@ choices="0 1"
#endif
// uking_edit_enable_wind_vtx_transform_scale  [0, 1]
#ifndef uking_edit_enable_wind_vtx_transform_scale
#define uking_edit_enable_wind_vtx_transform_scale 0 //@ choices="0 1"
#endif
// uking_enable_lumberjack  [0, 1]
#ifndef uking_enable_lumberjack
#define uking_enable_lumberjack 0 //@ choices="0 1"
#endif
// uking_enable_lumberjack_trunk  [0, 1]
#ifndef uking_enable_lumberjack_trunk
#define uking_enable_lumberjack_trunk 0 //@ choices="0 1"
#endif
// uking_enable_lumberjack_canopy  [0, 1]
#ifndef uking_enable_lumberjack_canopy
#define uking_enable_lumberjack_canopy 0 //@ choices="0 1"
#endif
// uking_lumberjack_annual_ring_albedo_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_lumberjack_annual_ring_albedo_color
#define uking_lumberjack_annual_ring_albedo_color 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_lumberjack_side_texcoord  [0, 1, 2, 6, 7, 8]
#ifndef uking_lumberjack_side_texcoord
#define uking_lumberjack_side_texcoord 0 //@ choices="0 1 2 6 7 8"
#endif
// uking_enable_inner_light  [0, 1]
#ifndef uking_enable_inner_light
#define uking_enable_inner_light 0 //@ choices="0 1"
#endif
// uking_inner_light_mode  [0, 1]
#ifndef uking_inner_light_mode
#define uking_inner_light_mode 0 //@ choices="0 1"
#endif
// uking_inner_light_source_position  [100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 0, 300, 400, 405, 401, 406, 402, 403, 404]
#ifndef uking_inner_light_source_position
#define uking_inner_light_source_position 100 //@ choices="100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 0 300 400 405 401 406 402 403 404"
#endif
// uking_inner_light_source_radius  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_inner_light_source_radius
#define uking_inner_light_source_radius 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_inner_light_diffusivity  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_inner_light_diffusivity
#define uking_inner_light_diffusivity 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_enable_inner_light_line  [0, 1]
#ifndef uking_enable_inner_light_line
#define uking_enable_inner_light_line 0 //@ choices="0 1"
#endif
// uking_inner_light_source_position_end  [100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 0, 300, 400, 405, 401, 406, 402, 403, 404]
#ifndef uking_inner_light_source_position_end
#define uking_inner_light_source_position_end 100 //@ choices="100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 0 300 400 405 401 406 402 403 404"
#endif
// uking_inner_light_source_radius_end  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_inner_light_source_radius_end
#define uking_inner_light_source_radius_end 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_force_export_normal  [0, 1]
#ifndef uking_force_export_normal
#define uking_force_export_normal 0 //@ choices="0 1"
#endif
// uking_force_export_localpos  [0, 1]
#ifndef uking_force_export_localpos
#define uking_force_export_localpos 0 //@ choices="0 1"
#endif
// uking_force_export_viewpos  [0, 1]
#ifndef uking_force_export_viewpos
#define uking_force_export_viewpos 0 //@ choices="0 1"
#endif
// uking_force_export_vtx_color  [0, 1]
#ifndef uking_force_export_vtx_color
#define uking_force_export_vtx_color 0 //@ choices="0 1"
#endif
// gsys_dynamic_force_zprepass  [0, 1]
#ifndef gsys_dynamic_force_zprepass
#define gsys_dynamic_force_zprepass 0 //@ choices="0 1"
#endif
// gsys_gbuffer_xlu  [0, 1]
#ifndef gsys_gbuffer_xlu
#define gsys_gbuffer_xlu 0 //@ choices="0 1"
#endif
// gsys_force_zprepass  [0, 1]
#ifndef gsys_force_zprepass
#define gsys_force_zprepass 0 //@ choices="0 1"
#endif
// uking_enable_gbuffer_xlu_blend  [0, 1]
#ifndef uking_enable_gbuffer_xlu_blend
#define uking_enable_gbuffer_xlu_blend 1 //@ choices="0 1"
#endif
// gsys_gbuffer_xlu_opa  [0, 1]
#ifndef gsys_gbuffer_xlu_opa
#define gsys_gbuffer_xlu_opa 0 //@ choices="0 1"
#endif
// enable_bake_texture  [0, 1]
#ifndef enable_bake_texture
#define enable_bake_texture 0 //@ choices="0 1"
#endif
// uking_map_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_map_color
#define uking_map_color 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_map_enable_fragment_normal  [0, 1]
#ifndef uking_map_enable_fragment_normal
#define uking_map_enable_fragment_normal 0 //@ choices="0 1"
#endif
// uking_enable_texcoord0  [0, 1]
#ifndef uking_enable_texcoord0
#define uking_enable_texcoord0 1 //@ choices="0 1"
#endif
// uking_texcoord0_mapping  [-1, 0, 1, 2, 3, 10, 11, 20, 21, 22, 23, 24, 25, 100, 101, 102, 103, 112, 113, 104, 105, 106, 107, 108, 109, 110, 111]
#ifndef uking_texcoord0_mapping
#define uking_texcoord0_mapping 0 //@ choices="-1 0 1 2 3 10 11 20 21 22 23 24 25 100 101 102 103 112 113 104 105 106 107 108 109 110 111"
#endif
// uking_texcoord0_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord0_srt
#define uking_texcoord0_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_texcoord0_offset_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord0_offset_srt
#define uking_texcoord0_offset_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_enable_texcoord1  [0, 1]
#ifndef uking_enable_texcoord1
#define uking_enable_texcoord1 0 //@ choices="0 1"
#endif
// uking_texcoord1_mapping  [-1, 0, 1, 2, 3, 10, 11, 20, 21, 22, 23, 24, 25, 100, 101, 102, 103, 112, 113, 104, 105, 106, 107, 108, 109, 110, 111]
#ifndef uking_texcoord1_mapping
#define uking_texcoord1_mapping 0 //@ choices="-1 0 1 2 3 10 11 20 21 22 23 24 25 100 101 102 103 112 113 104 105 106 107 108 109 110 111"
#endif
// uking_texcoord1_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord1_srt
#define uking_texcoord1_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_texcoord1_offset_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord1_offset_srt
#define uking_texcoord1_offset_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_enable_texcoord2  [0, 1]
#ifndef uking_enable_texcoord2
#define uking_enable_texcoord2 0 //@ choices="0 1"
#endif
// uking_texcoord2_mapping  [-1, 0, 1, 2, 3, 10, 11, 20, 21, 22, 23, 24, 25, 100, 101, 102, 103, 112, 113, 104, 105, 106, 107, 108, 109, 110, 111]
#ifndef uking_texcoord2_mapping
#define uking_texcoord2_mapping 0 //@ choices="-1 0 1 2 3 10 11 20 21 22 23 24 25 100 101 102 103 112 113 104 105 106 107 108 109 110 111"
#endif
// uking_texcoord2_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord2_srt
#define uking_texcoord2_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_texcoord2_offset_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord2_offset_srt
#define uking_texcoord2_offset_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_enable_texcoord3  [0, 1]
#ifndef uking_enable_texcoord3
#define uking_enable_texcoord3 0 //@ choices="0 1"
#endif
// uking_texcoord3_mapping  [-1, 0, 1, 2, 3, 10, 11, 20, 21, 22, 23, 24, 25, 100, 101, 102, 103, 112, 113, 104, 105, 106, 107, 108, 109, 110, 111]
#ifndef uking_texcoord3_mapping
#define uking_texcoord3_mapping 0 //@ choices="-1 0 1 2 3 10 11 20 21 22 23 24 25 100 101 102 103 112 113 104 105 106 107 108 109 110 111"
#endif
// uking_texcoord3_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord3_srt
#define uking_texcoord3_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_texcoord3_offset_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord3_offset_srt
#define uking_texcoord3_offset_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_enable_texcoord4  [0, 1]
#ifndef uking_enable_texcoord4
#define uking_enable_texcoord4 0 //@ choices="0 1"
#endif
// uking_texcoord4_mapping  [-1, 0, 1, 2, 3, 10, 11, 20, 21, 22, 23, 24, 25, 100, 101, 102, 103, 112, 113, 104, 105, 106, 107, 108, 109, 110, 111]
#ifndef uking_texcoord4_mapping
#define uking_texcoord4_mapping 0 //@ choices="-1 0 1 2 3 10 11 20 21 22 23 24 25 100 101 102 103 112 113 104 105 106 107 108 109 110 111"
#endif
// uking_texcoord4_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord4_srt
#define uking_texcoord4_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_texcoord4_offset_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord4_offset_srt
#define uking_texcoord4_offset_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_enable_texcoord5  [0, 1]
#ifndef uking_enable_texcoord5
#define uking_enable_texcoord5 0 //@ choices="0 1"
#endif
// uking_texcoord5_mapping  [-1, 0, 1, 2, 3, 10, 11, 20, 21, 22, 23, 24, 25, 100, 101, 102, 103, 112, 113, 104, 105, 106, 107, 108, 109, 110, 111]
#ifndef uking_texcoord5_mapping
#define uking_texcoord5_mapping 0 //@ choices="-1 0 1 2 3 10 11 20 21 22 23 24 25 100 101 102 103 112 113 104 105 106 107 108 109 110 111"
#endif
// uking_texcoord5_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord5_srt
#define uking_texcoord5_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_texcoord5_offset_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord5_offset_srt
#define uking_texcoord5_offset_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_texcoord_toon_spec_mapping  [-1, 0, 1, 2, 3, 10, 11, 20, 21, 22, 23, 24, 25, 100, 101, 102, 103, 112, 113, 104, 105, 106, 107, 108, 109, 110, 111]
#ifndef uking_texcoord_toon_spec_mapping
#define uking_texcoord_toon_spec_mapping 0 //@ choices="-1 0 1 2 3 10 11 20 21 22 23 24 25 100 101 102 103 112 113 104 105 106 107 108 109 110 111"
#endif
// uking_texcoord_toon_spec_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord_toon_spec_srt
#define uking_texcoord_toon_spec_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_texcoord_toon_spec_offset_srt  [-1, 0, 1, 2, 3, 4, 5]
#ifndef uking_texcoord_toon_spec_offset_srt
#define uking_texcoord_toon_spec_offset_srt -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// uking_enable_proctexcoord0  [0, 1]
#ifndef uking_enable_proctexcoord0
#define uking_enable_proctexcoord0 0 //@ choices="0 1"
#endif
// uking_proctexcoord0_mapping  [1, 0, 3, 2]
#ifndef uking_proctexcoord0_mapping
#define uking_proctexcoord0_mapping 0 //@ choices="1 0 3 2"
#endif
// uking_enable_proctexcoord1  [0, 1]
#ifndef uking_enable_proctexcoord1
#define uking_enable_proctexcoord1 0 //@ choices="0 1"
#endif
// uking_proctexcoord1_mapping  [1, 0, 3, 2]
#ifndef uking_proctexcoord1_mapping
#define uking_proctexcoord1_mapping 0 //@ choices="1 0 3 2"
#endif
// uking_enable_proctexcoord2  [0, 1]
#ifndef uking_enable_proctexcoord2
#define uking_enable_proctexcoord2 0 //@ choices="0 1"
#endif
// uking_proctexcoord2_mapping  [1, 0, 3, 2]
#ifndef uking_proctexcoord2_mapping
#define uking_proctexcoord2_mapping 0 //@ choices="1 0 3 2"
#endif
// uking_common_texcoord  [0, 1, 2, 6, 7, 8, 3, 4, 5]
#ifndef uking_common_texcoord
#define uking_common_texcoord 0 //@ choices="0 1 2 6 7 8 3 4 5"
#endif
// uking_enable_normalmap  [0, 1]
#ifndef uking_enable_normalmap
#define uking_enable_normalmap 1 //@ choices="0 1"
#endif
// uking_normal_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_normal_color
#define uking_normal_color 2 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_enable_decode_normalmap  [0, 1]
#ifndef uking_enable_decode_normalmap
#define uking_enable_decode_normalmap 1 //@ choices="0 1"
#endif
// uking_enable_normalmap2  [0, 1]
#ifndef uking_enable_normalmap2
#define uking_enable_normalmap2 0 //@ choices="0 1"
#endif
// uking_enable_backface_modify  [0, 1]
#ifndef uking_enable_backface_modify
#define uking_enable_backface_modify 0 //@ choices="0 1"
#endif
// uking_enable_calc_binormal_vertex  [0, 1]
#ifndef uking_enable_calc_binormal_vertex
#define uking_enable_calc_binormal_vertex 0 //@ choices="0 1"
#endif
// uking_normal_color2  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_normal_color2
#define uking_normal_color2 3 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_normalmap_blend_ratio  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_normalmap_blend_ratio
#define uking_normalmap_blend_ratio 104 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_normalmap_blend_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_normalmap_blend_channel
#define uking_normalmap_blend_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_normalmap_blend_method  [0, 1]
#ifndef uking_normalmap_blend_method
#define uking_normalmap_blend_method 0 //@ choices="0 1"
#endif
// uking_material_attribute  [0, 10]
#ifndef uking_material_attribute
#define uking_material_attribute 0 //@ choices="0 10"
#endif
// uking_material_behave  [0, 1, 2, 10, 100, 101, 104, 102, 103, 105]
#ifndef uking_material_behave
#define uking_material_behave 0 //@ choices="0 1 2 10 100 101 104 102 103 105"
#endif
// uking_chara_size  [0, 1, 2, 3, 4]
#ifndef uking_chara_size
#define uking_chara_size 2 //@ choices="0 1 2 3 4"
#endif
// uking_metal_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_metal_color
#define uking_metal_color 402 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_metal_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_metal_channel
#define uking_metal_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_albedo_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_albedo_color
#define uking_albedo_color 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_specmask_scaler  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_specmask_scaler
#define uking_specmask_scaler 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_specular_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_specular_channel
#define uking_specular_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_specular_hair  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_specular_hair
#define uking_specular_hair 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_specular_hair_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_specular_hair_channel
#define uking_specular_hair_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_emission  [0, 1]
#ifndef uking_enable_emission
#define uking_enable_emission 0 //@ choices="0 1"
#endif
// uking_emission_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_emission_color
#define uking_emission_color 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_enable_transmission  [0, 1]
#ifndef uking_enable_transmission
#define uking_enable_transmission 0 //@ choices="0 1"
#endif
// uking_transmission_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_transmission_color
#define uking_transmission_color 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_transmission_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_transmission_channel
#define uking_transmission_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_enable_ao  [0, 1]
#ifndef uking_enable_ao
#define uking_enable_ao 0 //@ choices="0 1"
#endif
// uking_ao_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_ao_color
#define uking_ao_color 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_ao_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_ao_channel
#define uking_ao_channel 10 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_grossy_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_grossy_color
#define uking_grossy_color 402 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_grossy_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_grossy_channel
#define uking_grossy_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_grossy_intensity  [1, 2, 3]
#ifndef uking_grossy_intensity
#define uking_grossy_intensity 2 //@ choices="1 2 3"
#endif
// uking_transparency_color  [0, 1, 2, 3, 4, 5, 6, 7, 100, 101, 102, 103, 112, 113, 114, 115, 104, 105, 106, 107, 108, 109, 110, 111, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 300, 8, 9, 504, 500, 501, 502, 503, 505, 506, 507, 518, 508, 517, 509, 510, 511, 512, 400, 405, 401, 406, 402, 403, 404, 513, 514, 515, 516]
#ifndef uking_transparency_color
#define uking_transparency_color 0 //@ choices="0 1 2 3 4 5 6 7 100 101 102 103 112 113 114 115 104 105 106 107 108 109 110 111 200 201 202 203 204 205 206 207 208 209 210 211 212 213 300 8 9 504 500 501 502 503 505 506 507 518 508 517 509 510 511 512 400 405 401 406 402 403 404 513 514 515 516"
#endif
// uking_transparency_channel  [0, 1, 10, 11, 20, 21, 30, 31, 40, 41, 1000, 1001, 2, 3, 12, 13, 22, 23, 32, 33, 42, 43, 1002, 1003, 2000]
#ifndef uking_transparency_channel
#define uking_transparency_channel 0 //@ choices="0 1 10 11 20 21 30 31 40 41 1000 1001 2 3 12 13 22 23 32 33 42 43 1002 1003 2000"
#endif
// uking_albedo_old  [0, 1]
#ifndef uking_albedo_old
#define uking_albedo_old 0 //@ choices="0 1"
#endif
// bake_shadow_type  [-1, 0, 1, 2, 3, 4, 5]
#ifndef bake_shadow_type
#define bake_shadow_type -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// bake_light_type  [-1, 0, 1, 2, 3, 4, 5]
#ifndef bake_light_type
#define bake_light_type -1 //@ choices="-1 0 1 2 3 4 5"
#endif
// gsys_renderstate  [0, 1, 2, 3]
#ifndef gsys_renderstate
#define gsys_renderstate 0 //@ render_info="gsys_render_state_mode" choices="opaque:0 mask:1 translucent:2 custom:3"
#endif
// gsys_pass  [0, 1, 2, 3]
#ifndef gsys_pass
#define gsys_pass 0 //@ choices="0 1 2 3"
#endif
// gsys_alpha_test_func  [0, 1, 2, 3, 4, 5, 6, 7]
#ifndef gsys_alpha_test_func
#define gsys_alpha_test_func 0 //@ render_info="gsys_alpha_test_func" choices="never:0 less:1 equal:2 lequal:3 greater:4 notequal:5 gequal:6 always:7"
#endif
// gsys_alpha_test_enable  [0, 1]
#ifndef gsys_alpha_test_enable
#define gsys_alpha_test_enable 0 //@ render_info="gsys_alpha_test_enable" choices="false:0 true:1"
#endif

// ==================== DYNAMIC OPTIONS (4)
// gsys_weight  [-1, 0, 1, 2, 3, 4, 5, 6, 7, 8]
#ifndef gsys_weight
#define gsys_weight -1 //@ choices="-1 0 1 2 3 4 5 6 7 8" branch="dynamic" is_skin_count="1"
#endif
// gsys_assign_type  enum: GSYS_ASSIGN_VISUALIZE=0, GSYS_ASSIGN_MATERIAL=1, GSYS_ASSIGN_ZONLY=2, GSYS_ASSIGN_GBUFFER=3
#define GSYS_ASSIGN_VISUALIZE 0
#define GSYS_ASSIGN_MATERIAL 1
#define GSYS_ASSIGN_ZONLY 2
#define GSYS_ASSIGN_GBUFFER 3
#ifndef gsys_assign_type
#define gsys_assign_type 1 //@ choices="gsys_assign_visualize gsys_assign_material gsys_assign_zonly gsys_assign_gbuffer" branch="dynamic" type="string" flags="compile_all_choices"
#endif
// gsys_assign_variation  [0, 1, 2]
#ifndef gsys_assign_variation
#define gsys_assign_variation 0 //@ choices="0 1 2" branch="dynamic" flags="compile_all_choices"
#endif
// system_id  [0]
#ifndef system_id
#define system_id 0 //@ choices="0" branch="dynamic"
#endif

#endif
