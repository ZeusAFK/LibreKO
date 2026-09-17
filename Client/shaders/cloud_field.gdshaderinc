uniform sampler2D cloud_tex : filter_linear_mipmap, repeat_enable;
uniform vec2 cloud_pan = vec2(0.0);                      // wind displacement, world units
uniform float cloud_scale = 0.0004;                      // world units -> noise UV
uniform float cloud_cover : hint_range(0.0, 1.0) = 0.4;  // 0 clear .. 1 solid overcast

// Coverage 0..1 of the deck at a point ON the deck (world XZ). A soft base blob plus a smaller
// detail octave that breaks up its silhouette; `cover` slides the threshold, so raising it grows
// the existing clouds outward instead of fading new ones in. `lod` blurs the result (the ground
// shadow wants a softer edge than the cloud itself, and a higher mip is cheaper to sample).
float cloud_cover_at(vec2 w, float lod) {
    float base = textureLod(cloud_tex, (w - cloud_pan) * cloud_scale, lod).r;
    // The detail octave is panned at 2x so it SHEARS against the base — that relative drift is what
    // makes a deck look alive rather than like one sliding photograph. 2 and 3 are integers, so both
    // layers still land back on themselves when Sky wraps cloud_pan (no jump on the wrap). Its weight
    // is deliberately small: heavier and it stops roughening the silhouette and starts punching holes
    // through the middle of every cloud, which reads as torn tissue paper rather than as cumulus.
    float det = textureLod(cloud_tex, (w - cloud_pan * 2.0) * (cloud_scale * 3.0), lod).g;
    return clamp(smoothstep(0.0, 0.22, base * 0.82 + det * 0.18 - (1.0 - cloud_cover)), 0.0, 1.0);
}

// Base-octave-only coverage: half the taps, used where the detail can't be seen anyway (the
// self-shadow probe inside the sky shader). 0.09 stands in for the detail term's average.
float cloud_cover_base(vec2 w, float lod) {
    float base = textureLod(cloud_tex, (w - cloud_pan) * cloud_scale, lod).r;
    return clamp(smoothstep(0.0, 0.22, base * 0.82 + 0.09 - (1.0 - cloud_cover)), 0.0, 1.0);
}
