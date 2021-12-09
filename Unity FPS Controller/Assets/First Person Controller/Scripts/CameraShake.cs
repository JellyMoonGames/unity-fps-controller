using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraShake : MonoBehaviour
{
    #region Public Properties

    public float ShakeSpeed { get; private set; }
    public float ShakeMultiplier { get; private set; }
    public float ShakeAmountCooldownRate { get; private set; }
    public bool IsShaking { get; private set; }

    #endregion

    #region Private Variables

    private Vector3 initialPosition = Vector3.zero;
    private Vector3 newPosition = Vector3.zero;
    private float amplitude = 0f;

    #endregion

    private void Start()
    {
        IsShaking = false;
        initialPosition = transform.localPosition;
        newPosition = initialPosition;
        amplitude = 0f;
    }

    private void Update()
    {
        if (IsShaking)
        {
            amplitude -= ShakeAmountCooldownRate * Time.deltaTime;
            if (amplitude <= 0f) amplitude = 0f;

            newPosition = initialPosition + Random.insideUnitSphere * (amplitude * ShakeMultiplier);
            transform.localPosition = Vector3.Lerp(transform.localPosition, newPosition, ShakeSpeed * Time.deltaTime);
        }
        else
        {
            transform.localPosition = Vector3.MoveTowards(transform.localPosition, initialPosition, 10f * Time.deltaTime);
        }
    }

    public void Shake(float amplitude, float shakeSpeed, float shakeMultiplier = 1f, float duration = 0f)
    {
        IsShaking = true;
        this.amplitude = amplitude;
        this.ShakeSpeed = shakeSpeed;
        this.ShakeMultiplier = shakeMultiplier;

        CancelInvoke("Shake");
        if (duration != 0) Invoke("StopShaking", duration);
    }

    public void StopShaking() => IsShaking = false;
}