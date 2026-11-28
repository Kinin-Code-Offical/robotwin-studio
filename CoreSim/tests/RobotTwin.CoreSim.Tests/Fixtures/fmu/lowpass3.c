/* The same first-order lag as lowpass.c as an FMI 3.0 co-simulation FMU, written for RobotWin's tests:
   y' = (k*u - y) / tau, stepped exactly over each communication interval. Variables (value references):
   0 u (Float64 input), 1 y (Float64 output), 2 k (Float64 parameter), 3 tau (Float64 parameter), 4 steps (Int32 output),
   5 saturated (Boolean output, |y| > limit), 6 limit (Float64 parameter), 7 gain32 (Float32 parameter, multiplies k).
   It logs through the importer's callback and checks the instantiation token and the resource path's separator. */
#include <math.h>
#include <stddef.h>
#include <stdlib.h>
#include <string.h>
#include <stdbool.h>
#include <stdint.h>

typedef void *fmi3Instance;
typedef void *fmi3InstanceEnvironment;
typedef unsigned int fmi3ValueReference;
typedef double fmi3Float64;
typedef float fmi3Float32;
typedef int32_t fmi3Int32;
typedef bool fmi3Boolean;
typedef const char *fmi3String;
typedef enum { fmi3OK, fmi3Warning, fmi3Discard, fmi3Error, fmi3Fatal } fmi3Status;
typedef void (*fmi3LogMessageCallback)(fmi3InstanceEnvironment, fmi3Status, fmi3String, fmi3String);

#define EXPORT __declspec(dllexport)
#define TOKEN "{3c1e9b2a-5d7f-4e21-9a43-lowpass3}"

typedef struct
{
    double u, y, k, tau, limit;
    float gain32;
    int steps;
    bool saturated;
    fmi3LogMessageCallback log;
    fmi3InstanceEnvironment env;
} Model;

EXPORT fmi3Instance fmi3InstantiateCoSimulation(fmi3String name, fmi3String token, fmi3String resources, fmi3Boolean visible,
    fmi3Boolean logging, fmi3Boolean eventMode, fmi3Boolean earlyReturn, const fmi3ValueReference required[], size_t nRequired,
    fmi3InstanceEnvironment env, fmi3LogMessageCallback log, void *intermediateUpdate)
{
    Model *m;
    size_t n;
    if (token == NULL || strcmp(token, TOKEN) != 0) return NULL;
    n = resources != NULL ? strlen(resources) : 0;
    if (n == 0 || (resources[n - 1] != '\\' && resources[n - 1] != '/')) return NULL;   /* FMI 3.0: a path ending in its separator */
    m = (Model *)calloc(1, sizeof(Model));
    if (m == NULL) return NULL;
    m->k = 1;
    m->tau = 0.01;
    m->limit = 4.5;
    m->gain32 = 1.0f;
    m->log = log;
    m->env = env;
    if (log != NULL) log(env, fmi3OK, "info", "lowpass3 ready");
    return m;
}

EXPORT fmi3Status fmi3EnterInitializationMode(fmi3Instance c, fmi3Boolean td, fmi3Float64 tol, fmi3Float64 start, fmi3Boolean sd, fmi3Float64 stop) { return fmi3OK; }
EXPORT fmi3Status fmi3ExitInitializationMode(fmi3Instance c) { return fmi3OK; }
EXPORT fmi3Status fmi3Terminate(fmi3Instance c) { return fmi3OK; }
EXPORT void fmi3FreeInstance(fmi3Instance c) { free(c); }

EXPORT fmi3Status fmi3SetFloat64(fmi3Instance c, const fmi3ValueReference vr[], size_t nvr, const fmi3Float64 v[], size_t nv)
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < nvr; i++)
    {
        switch (vr[i])
        {
            case 0: m->u = v[i]; break;
            case 2: m->k = v[i]; break;
            case 3:
                if (v[i] <= 0) return fmi3Error;
                m->tau = v[i];
                break;
            case 6: m->limit = v[i]; break;
            default: return fmi3Error;
        }
    }

    return fmi3OK;
}

EXPORT fmi3Status fmi3GetFloat64(fmi3Instance c, const fmi3ValueReference vr[], size_t nvr, fmi3Float64 v[], size_t nv)
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < nvr; i++)
    {
        switch (vr[i])
        {
            case 0: v[i] = m->u; break;
            case 1: v[i] = m->y; break;
            case 2: v[i] = m->k; break;
            case 3: v[i] = m->tau; break;
            case 6: v[i] = m->limit; break;
            default: return fmi3Error;
        }
    }

    return fmi3OK;
}

EXPORT fmi3Status fmi3SetFloat32(fmi3Instance c, const fmi3ValueReference vr[], size_t nvr, const fmi3Float32 v[], size_t nv)
{
    Model *m = (Model *)c;
    if (nvr != 1 || vr[0] != 7) return fmi3Error;
    m->gain32 = v[0];
    return fmi3OK;
}

EXPORT fmi3Status fmi3GetFloat32(fmi3Instance c, const fmi3ValueReference vr[], size_t nvr, fmi3Float32 v[], size_t nv)
{
    Model *m = (Model *)c;
    if (nvr != 1 || vr[0] != 7) return fmi3Error;
    v[0] = m->gain32;
    return fmi3OK;
}

EXPORT fmi3Status fmi3GetInt32(fmi3Instance c, const fmi3ValueReference vr[], size_t nvr, fmi3Int32 v[], size_t nv)
{
    Model *m = (Model *)c;
    if (nvr != 1 || vr[0] != 4) return fmi3Error;
    v[0] = m->steps;
    return fmi3OK;
}

EXPORT fmi3Status fmi3GetBoolean(fmi3Instance c, const fmi3ValueReference vr[], size_t nvr, fmi3Boolean v[], size_t nv)
{
    Model *m = (Model *)c;
    if (nvr != 1 || vr[0] != 5) return fmi3Error;
    v[0] = m->saturated;
    return fmi3OK;
}

EXPORT fmi3Status fmi3DoStep(fmi3Instance c, fmi3Float64 t, fmi3Float64 h, fmi3Boolean noSetPrior, fmi3Boolean *eventNeeded,
    fmi3Boolean *terminate, fmi3Boolean *early, fmi3Float64 *last)
{
    Model *m = (Model *)c;
    double target = m->k * m->gain32 * m->u;
    m->y = target + (m->y - target) * exp(-h / m->tau);   /* exact for a constant input over the step */
    m->steps++;
    m->saturated = fabs(m->y) > m->limit;
    *eventNeeded = false;
    *terminate = false;
    *early = false;
    *last = t + h;
    return fmi3OK;
}
