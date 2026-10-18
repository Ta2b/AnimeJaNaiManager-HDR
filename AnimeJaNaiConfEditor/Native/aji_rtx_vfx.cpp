// aji_rtx for Linux: NVIDIA TrueHDR through the Maxine Video Effects SDK, on CUDA device memory.
// Same three entry points as the Windows library (NGX, RTX Video SDK), plus aji_rtx_input_bits:
// the Maxine filter takes 8-bit RGBA, where the Windows one takes packed 10-bit RGB.
// Super Resolution is not provided here: create fails when it is requested.
//
// Needs the SDK core (libVideoFX.so, libNVCVImage.so) and the nvvfxtruehdr feature, installed
// by the user with their NGC key. Nothing from the SDK is redistributed.
#include <dlfcn.h>
#include <glob.h>
#include <stdio.h>
#include <stdlib.h>
#include <string>

#include "nvCVImage.h"
#include "nvVideoEffects.h"
#include "nvVFXTrueHDR.h"

#define RTX_EXPORT extern "C" __attribute__((visibility("default")))

static NvVFX_Handle g_fx = nullptr;
static bool g_loaded = false;
static int g_w = 0, g_h = 0;
static unsigned g_set[4] = {~0u, ~0u, ~0u, ~0u};
static void *g_in = nullptr, *g_out = nullptr;

static void say(const char *what, NvCV_Status st)
{
    fprintf(stderr, "aji_rtx (Maxine TrueHDR): %s: %s\n", what, NvCV_GetErrorStringFromCode(st));
}

static std::string sdk_root()
{
    const char *env = getenv("ANIMEJANAI_VFX_SDK");
    return env && *env ? env : "/usr/local/VideoFX";
}

// libVideoFX opens the feature library by name. The player was not started with the feature
// directories in LD_LIBRARY_PATH, so they are opened here by path first.
static void preload_features()
{
    static bool done = false;
    if (done)
        return;
    done = true;
    const std::string pattern = sdk_root() + "/features/*/lib/*.so*";
    glob_t g = {};
    if (glob(pattern.c_str(), 0, nullptr, &g) == 0)
        for (size_t i = 0; i < g.gl_pathc; i++)
            dlopen(g.gl_pathv[i], RTLD_NOW | RTLD_GLOBAL);
    globfree(&g);
}

static void destroy()
{
    if (g_fx)
        NvVFX_DestroyEffect(g_fx);
    g_fx = nullptr;
    g_loaded = false;
    g_w = g_h = 0;
    g_in = g_out = nullptr;
    for (unsigned &v : g_set)
        v = ~0u;
}

RTX_EXPORT int aji_rtx_input_bits(void)
{
    return 8;
}

// The CUDA context must be current on the calling thread. Returns 1 on success.
RTX_EXPORT int aji_rtx_create(void *cu_context, void *cu_stream, int thdr, int vsr)
{
    (void)cu_context;
    (void)cu_stream;
    destroy();
    if (vsr) {
        fprintf(stderr, "aji_rtx (Maxine TrueHDR): Super Resolution is not available on Linux; "
                        "turn it off to use TrueHDR\n");
        return 0;
    }
    if (!thdr)
        return 0;
    preload_features();
    NvCV_Status st = NvVFX_CreateEffect(NVVFX_FX_TRUE_HDR, &g_fx);
    if (st != NVCV_SUCCESS) {
        say("the TrueHDR feature could not be created (is nvvfxtruehdr installed?)", st);
        g_fx = nullptr;
        return 0;
    }
    const std::string models = sdk_root() + "/lib/models";
    NvVFX_SetString(g_fx, NVVFX_MODEL_DIRECTORY, models.c_str());
    return 1;
}

static bool set_tunables(unsigned contrast, unsigned saturation, unsigned middle_gray,
                         unsigned peak)
{
    const unsigned want[4] = {contrast, saturation, middle_gray, peak};
    const char *const names[4] = {NVVFXTRUEHDR_CONTRAST, NVVFXTRUEHDR_SATURATION,
                                  NVVFXTRUEHDR_MIDDLE_GRAY, NVVFXTRUEHDR_LUMINANCE};
    for (int i = 0; i < 4; i++) {
        if (g_set[i] == want[i])
            continue;
        const NvCV_Status st = NvVFX_SetU32(g_fx, names[i], want[i]);
        if (st != NVCV_SUCCESS) {
            say(names[i], st);
            return false;
        }
        g_set[i] = want[i];
    }
    return true;
}

// in:  RGBA 8-bit interleaved, SDR, stride 4 * iw (see aji_rtx_input_bits).
// out: packed 10-bit RGB (R in the low bits), HDR10 (PQ, BT.2020), stride 4 * ow.
// TrueHDR does not scale: ow x oh must equal iw x ih.
RTX_EXPORT int aji_rtx_eval(void *in, void *out, int iw, int ih, int ow, int oh, int vsr_quality,
                            int contrast, int saturation, int middle_gray, int max_luminance)
{
    (void)vsr_quality;
    if (!g_fx || iw != ow || ih != oh)
        return 0;
    NvCV_Status st;
    if (!g_loaded || iw != g_w || ih != g_h || in != g_in || out != g_out) {
        NvCVImage src, dst;
        st = NvCVImage_Init(&src, (unsigned)iw, (unsigned)ih, iw * 4, in, NVCV_RGBA, NVCV_U8,
                            NVCV_INTERLEAVED, NVCV_GPU);
        if (st == NVCV_SUCCESS)
            st = NvCVImage_Init(&dst, (unsigned)ow, (unsigned)oh, ow * 4, out, NVCV_RGB10A2,
                                NVCV_P32, NVCV_INTERLEAVED, NVCV_GPU);
        if (st == NVCV_SUCCESS)
            st = NvVFX_SetImage(g_fx, NVVFX_INPUT_IMAGE, &src);
        if (st == NVCV_SUCCESS)
            st = NvVFX_SetImage(g_fx, NVVFX_OUTPUT_IMAGE, &dst);
        if (st != NVCV_SUCCESS) {
            say("image binding", st);
            return 0;
        }
        g_in = in;
        g_out = out;
    }
    if (!set_tunables((unsigned)contrast, (unsigned)saturation, (unsigned)middle_gray,
                      (unsigned)max_luminance))
        return 0;
    if (!g_loaded || iw != g_w || ih != g_h) {
        // Default CUDA stream: the engine synchronises its own stream before and after.
        NvVFX_SetCudaStream(g_fx, NVVFX_CUDA_STREAM, nullptr);
        st = NvVFX_Load(g_fx);
        if (st != NVCV_SUCCESS) {
            say("model load", st);
            return 0;
        }
        g_loaded = true;
        g_w = iw;
        g_h = ih;
    }
    st = NvVFX_Run(g_fx, 0);
    if (st != NVCV_SUCCESS) {
        say("run", st);
        return 0;
    }
    return 1;
}

RTX_EXPORT void aji_rtx_shutdown(void)
{
    destroy();
}
