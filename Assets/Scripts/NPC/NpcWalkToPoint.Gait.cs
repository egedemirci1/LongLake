using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

// Partial: split for maintainability. Type identity unchanged.
public partial class NpcWalkToPoint : NetworkBehaviour
{
    private void UpdateGait()
    {
        if (_traversingOffMeshLink) return;
        if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh || _agent.isStopped)
            return;

        // Araba bacağı (varıştan önce) veya Safiye bacağı.
        bool onCarLeg = _journeyInitialized && !journeyArrived.Value;
        bool onSafiyeLeg = _safiyeJourneyInitialized && safiyeJourneyStarted.Value;
        if (!onCarLeg && !onSafiyeLeg)
            return;

        _journeyElapsed += Time.deltaTime;

        if (_agent.pathPending)
        {
            ApplyGait(running: false);
            return;
        }

        float remaining = _agent.remainingDistance;
        // Unity bazen hasPath iken bile Infinity döner — o zaman düz mesafe kullan.
        if (!_agent.hasPath || float.IsInfinity(remaining) || float.IsNaN(remaining))
        {
            if (_agent.hasPath)
                remaining = Vector3.Distance(_agent.transform.position, _agent.destination);
            else
            {
                ApplyGait(running: false);
                return;
            }
        }

        bool nearEnd = remaining <= Mathf.Max(approachWalkDistance, stoppingDistance + 0.5f);
        bool wantRun = !nearEnd && _journeyElapsed >= walkBeforeRunSeconds;
        bool canRun = wantRun && (!useRunStamina || !_runExhausted);
        ApplyGait(canRun);
        TickRunStamina(Time.deltaTime);
    }

    private void ResetRunStamina()
    {
        _runStamina = maxRunStamina;
        _runExhausted = false;
    }

    private void TickRunStamina(float dt)
    {
        if (!useRunStamina || dt <= 0f) return;

        if (_isRunning)
        {
            _runStamina -= runStaminaDrainPerSecond * dt;
            if (_runStamina <= 0f)
            {
                _runStamina = 0f;
                _runExhausted = true;
                ApplyGait(running: false);
            }
            return;
        }

        // Yürüyüş / duruşta doldur; full olunca tekrar koşabilir.
        _runStamina += runStaminaRegenPerSecond * dt;
        if (_runStamina >= maxRunStamina)
        {
            _runStamina = maxRunStamina;
            _runExhausted = false;
        }
    }

    private void UpdateAnimation()
    {
        if (_agent == null || _animator == null)
            return;

        bool onCarLeg = _journeyInitialized && !journeyArrived.Value;
        bool onSafiyeLeg = _safiyeJourneyInitialized && safiyeJourneyStarted.Value;
        if (!onCarLeg && !onSafiyeLeg)
            return;

        float mult = _isRunning ? runAnimatorSpeedMultiplier : walkAnimatorSpeedMultiplier;
        float animationSpeed = _agent.enabled && _agent.isOnNavMesh && !_agent.isStopped
            ? _agent.velocity.magnitude * mult
            : 0f;
        _animator.SetFloat(SpeedHash, animationSpeed, 0.12f, Time.deltaTime);
    }

    private void ApplyGait(bool running)
    {
        if (_agent == null) return;

        if (_isRunning == running &&
            Mathf.Approximately(_agent.speed, running ? runSpeed : walkSpeed))
            return;

        _isRunning = running;
        _agent.speed = running ? runSpeed : walkSpeed;
        _agent.acceleration = running ? 10f : 5f;
        float rem = _agent.remainingDistance;
        bool near = !float.IsInfinity(rem) && rem < approachWalkDistance;
        _agent.autoBraking = !running || near;
    }

}
