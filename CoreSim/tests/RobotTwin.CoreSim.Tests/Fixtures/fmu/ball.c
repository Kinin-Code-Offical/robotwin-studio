/* A bouncing ball as an FMI 2.0 model-exchange FMU, written for RobotWin's tests: the importer integrates
   h' = v, v' = -g; the event indicator h finds the floor (v becomes -e v there), and a time event every `tick`
   seconds counts ticks. Variables (value references): 0 h, 1 der(h), 2 v, 3 der(v), 4 g, 5 e (parameters),
   6 bounces, 7 ticks (Integer outputs), 8 tick (parameter, s). */
#include <math.h>
#include <stddef.h>
#include <string.h>

typedef void *fmi2Component;
typedef void *fmi2ComponentEnvironment;
typedef unsigned int fmi2ValueReference;
typedef double fmi2Real;
typedef int fmi2Integer;
typedef int fmi2Boolean;
typedef const char *fmi2String;
typedef enum { fmi2OK, fmi2Warning, fmi2Discard, fmi2Error, fmi2Fatal, fmi2Pending } fmi2Status;
typedef void (*fmi2CallbackLogger)(fmi2ComponentEnvironment, fmi2String, fmi2Status, fmi2String, fmi2String, ...);
typedef void *(*fmi2CallbackAllocateMemory)(size_t, size_t);
typedef void (*fmi2CallbackFreeMemory)(void *);
typedef struct
{
    fmi2CallbackLogger logger;
    fmi2CallbackAllocateMemory allocateMemory;
    fmi2CallbackFreeMemory freeMemory;
    void *stepFinished;
    fmi2ComponentEnvironment componentEnvironment;
} fmi2CallbackFunctions;
typedef struct
{
    fmi2Boolean newDiscreteStatesNeeded;
    fmi2Boolean terminateSimulation;
    fmi2Boolean nominalsOfContinuousStatesChanged;
    fmi2Boolean valuesOfContinuousStatesChanged;
    fmi2Boolean nextEventTimeDefined;
    fmi2Real nextEventTime;
} fmi2EventInfo;

#define EXPORT __declspec(dllexport)
#define GUID "{3b0f6a52-6f1e-4c55-9d1e-0b5a11c0ba11}"

typedef struct
{
    double time, h, v, g, e, tick;
    int bounces, ticks;
    const fmi2CallbackFunctions *cb;
} Model;

EXPORT fmi2Component fmi2Instantiate(fmi2String name, int type, fmi2String guid, fmi2String resources,
                                     const fmi2CallbackFunctions *f, fmi2Boolean visible, fmi2Boolean logging)
{
    Model *m;
    if (type != 0 || guid == NULL || strcmp(guid, GUID) != 0) return NULL;   /* model exchange only */
    m = (Model *)f->allocateMemory(1, sizeof(Model));
    if (m == NULL) return NULL;
    m->h = 1;
    m->g = 9.81;
    m->e = 0.7;
    m->tick = 0.25;
    m->cb = f;
    return m;
}

EXPORT void fmi2FreeInstance(fmi2Component c)
{
    Model *m = (Model *)c;
    if (m != NULL) m->cb->freeMemory(m);
}

EXPORT fmi2Status fmi2SetupExperiment(fmi2Component c, fmi2Boolean td, fmi2Real tol, fmi2Real start, fmi2Boolean sd, fmi2Real stop)
{
    ((Model *)c)->time = start;
    return fmi2OK;
}
EXPORT fmi2Status fmi2EnterInitializationMode(fmi2Component c) { return fmi2OK; }
EXPORT fmi2Status fmi2ExitInitializationMode(fmi2Component c) { return fmi2OK; }
EXPORT fmi2Status fmi2Terminate(fmi2Component c) { return fmi2OK; }

EXPORT fmi2Status fmi2SetReal(fmi2Component c, const fmi2ValueReference vr[], size_t n, const fmi2Real v[])
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < n; i++)
    {
        switch (vr[i])
        {
            case 0: m->h = v[i]; break;
            case 2: m->v = v[i]; break;
            case 4: m->g = v[i]; break;
            case 5: m->e = v[i]; break;
            case 8: m->tick = v[i]; break;
            default: return fmi2Error;
        }
    }
    return fmi2OK;
}

EXPORT fmi2Status fmi2GetReal(fmi2Component c, const fmi2ValueReference vr[], size_t n, fmi2Real v[])
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < n; i++)
    {
        switch (vr[i])
        {
            case 0: v[i] = m->h; break;
            case 1: v[i] = m->v; break;
            case 2: v[i] = m->v; break;
            case 3: v[i] = -m->g; break;
            case 4: v[i] = m->g; break;
            case 5: v[i] = m->e; break;
            case 8: v[i] = m->tick; break;
            default: return fmi2Error;
        }
    }
    return fmi2OK;
}

EXPORT fmi2Status fmi2SetInteger(fmi2Component c, const fmi2ValueReference vr[], size_t n, const fmi2Integer v[]) { return n == 0 ? fmi2OK : fmi2Error; }

EXPORT fmi2Status fmi2GetInteger(fmi2Component c, const fmi2ValueReference vr[], size_t n, fmi2Integer v[])
{
    Model *m = (Model *)c;
    size_t i;
    for (i = 0; i < n; i++)
    {
        if (vr[i] == 6) v[i] = m->bounces;
        else if (vr[i] == 7) v[i] = m->ticks;
        else return fmi2Error;
    }
    return fmi2OK;
}

EXPORT fmi2Status fmi2SetBoolean(fmi2Component c, const fmi2ValueReference vr[], size_t n, const fmi2Boolean v[]) { return n == 0 ? fmi2OK : fmi2Error; }
EXPORT fmi2Status fmi2GetBoolean(fmi2Component c, const fmi2ValueReference vr[], size_t n, fmi2Boolean v[]) { return n == 0 ? fmi2OK : fmi2Error; }

EXPORT fmi2Status fmi2SetTime(fmi2Component c, fmi2Real t)
{
    ((Model *)c)->time = t;
    return fmi2OK;
}

EXPORT fmi2Status fmi2SetContinuousStates(fmi2Component c, const fmi2Real x[], size_t nx)
{
    Model *m = (Model *)c;
    if (nx != 2) return fmi2Error;
    m->h = x[0];
    m->v = x[1];
    return fmi2OK;
}

EXPORT fmi2Status fmi2GetContinuousStates(fmi2Component c, fmi2Real x[], size_t nx)
{
    Model *m = (Model *)c;
    if (nx != 2) return fmi2Error;
    x[0] = m->h;
    x[1] = m->v;
    return fmi2OK;
}

EXPORT fmi2Status fmi2GetNominalsOfContinuousStates(fmi2Component c, fmi2Real x[], size_t nx)
{
    if (nx != 2) return fmi2Error;
    x[0] = 1;
    x[1] = 1;
    return fmi2OK;
}

EXPORT fmi2Status fmi2GetDerivatives(fmi2Component c, fmi2Real dx[], size_t nx)
{
    Model *m = (Model *)c;
    if (nx != 2) return fmi2Error;
    dx[0] = m->v;
    dx[1] = -m->g;
    return fmi2OK;
}

EXPORT fmi2Status fmi2GetEventIndicators(fmi2Component c, fmi2Real z[], size_t nz)
{
    Model *m = (Model *)c;
    if (nz != 1) return fmi2Error;
    z[0] = m->h;
    return fmi2OK;
}

EXPORT fmi2Status fmi2EnterEventMode(fmi2Component c) { return fmi2OK; }
EXPORT fmi2Status fmi2EnterContinuousTimeMode(fmi2Component c) { return fmi2OK; }

EXPORT fmi2Status fmi2NewDiscreteStates(fmi2Component c, fmi2EventInfo *info)
{
    Model *m = (Model *)c;
    double next;
    info->newDiscreteStatesNeeded = 0;
    info->terminateSimulation = 0;
    info->nominalsOfContinuousStatesChanged = 0;
    info->valuesOfContinuousStatesChanged = 0;
    if (m->h <= 0 && m->v < 0)   /* the floor: bounce */
    {
        m->h = 0;
        m->v = -m->e * m->v;
        m->bounces++;
        info->valuesOfContinuousStatesChanged = 1;
    }
    next = (m->ticks + 1) * m->tick;
    if (m->tick > 0 && m->time >= next - 1e-9)
    {
        m->ticks++;
        next = (m->ticks + 1) * m->tick;
    }
    info->nextEventTimeDefined = m->tick > 0;
    info->nextEventTime = next;
    return fmi2OK;
}

EXPORT fmi2Status fmi2CompletedIntegratorStep(fmi2Component c, fmi2Boolean noSetPrior, fmi2Boolean *enterEventMode, fmi2Boolean *terminate)
{
    *enterEventMode = 0;
    *terminate = 0;
    return fmi2OK;
}
