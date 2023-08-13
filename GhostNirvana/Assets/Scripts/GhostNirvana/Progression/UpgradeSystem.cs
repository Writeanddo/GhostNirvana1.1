using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using ScriptableBehaviour;
using Utils;

namespace GhostNirvana.Upgrade {

public class UpgradeSystem : MonoBehaviour {
    [SerializeField] ScriptableInt currentHealth; // use to check for death

    [SerializeField] LinearLimiterFloat experience;
    [SerializeField] LinearInt applianceCollectionAmount;
    [SerializeField] Bank bank;
    [SerializeField, Required] RectTransform levelUpOptionPanel;
    [SerializeField] Buff levelUpBuff;
    [SerializeField] UpgradeOptionDetails upgradeDetails;
    [SerializeField] UpgradeMoneyDisplay upgradeMoneyDetails;
    [SerializeField] int wage;
    bool levelUpSequenceRunning;
    List<UpgradeOption> upgradeOptions = new List<UpgradeOption>();

    [SerializeField, ReorderableList]
    List<Buff> buffOptions = new List<Buff>();

    Dictionary<Buff, int> buffsTaken = new Dictionary<Buff, int>();

    ApplianceCollector collector;

    void Awake() {
        collector = GetComponentInChildren<ApplianceCollector>();

        upgradeOptions.AddRange(GetComponentsInChildren<UpgradeOption>());
        levelUpOptionPanel.gameObject.SetActive(false);
    }

    void Update() {
        bool shouldLevelUp = !levelUpSequenceRunning && experience.Value >= experience.Limiter && currentHealth.Value > 0;
        if (shouldLevelUp) StartLevelUpSequence();
    }

    void StartLevelUpSequence() {
        int amountCollected;
        int moneyEarned;
        (amountCollected, moneyEarned) = collector.Collect((int) applianceCollectionAmount.Value);
        moneyEarned += wage;
        upgradeMoneyDetails.SetPaymentDescription(amountCollected, moneyEarned);
        bank.Deposit(wage);

        levelUpOptionPanel.gameObject.SetActive(true);
        levelUpSequenceRunning = true;

        int upgradeOptionsCount = upgradeOptions.Count;

        IEnumerator<Buff> randomBuffs = GetRandomBuffs();

        foreach (UpgradeOption upgradeOption in upgradeOptions) {
            Buff buffChosen = randomBuffs.MoveNext() ?
                randomBuffs.Current : buffOptions[Random.Range(0, buffOptions.Count)];
            upgradeOption.Initialize(buffChosen);
        }

        experience.Value -= experience.Limiter;


        Time.timeScale = 0;
    }

    IEnumerator<Buff> GetRandomBuffs() {
        for (int excludeIndex = 0; excludeIndex < buffOptions.Count; excludeIndex++) {
            int numAvailable = 0;
            float totalWeight = 0;

            for (int i = excludeIndex; i < buffOptions.Count; i++) {
                float weight = ComputeWeight(buffOptions[i],
                    chooseOnlyAffordable: excludeIndex == 0);
                totalWeight += weight;
                numAvailable += weight > 0 ? 1 : 0;
            }

            if (numAvailable == 0) yield break;

            float value = Mathx.RandomRange(0, totalWeight);

            int lastIndex = 0;
            bool buffChosen = false;
            for (int i = excludeIndex; i < buffOptions.Count; i++) {
                float weight = ComputeWeight(buffOptions[i],
                    chooseOnlyAffordable: excludeIndex == 0);
                if (weight == 0) continue;
                lastIndex = i;
                if (value <= weight) {
                    buffChosen = true;
                    yield return buffOptions[i];
                    if (i != excludeIndex)
                        (buffOptions[i], buffOptions[excludeIndex]) = (buffOptions[excludeIndex], buffOptions[i]);
                    break;
                }
                value -= weight;
            }

            if (!buffChosen) {
                yield return buffOptions[lastIndex];
                if (lastIndex != excludeIndex)
                    (buffOptions[lastIndex], buffOptions[excludeIndex]) = (buffOptions[excludeIndex], buffOptions[lastIndex]);
            }
        }
    }

    float ComputeWeight(Buff buff, bool chooseOnlyAffordable) {
        buffsTaken.TryGetValue(buff, out int numberOfTimesThisBuffTaken);

        bool takenEnoughTimes = buff.purchaseLimit > 0 && numberOfTimesThisBuffTaken >= buff.purchaseLimit;
        if (takenEnoughTimes) return 0;

        bool hasAllPrereqs = true;
        foreach (Buff prereq in buff.Prerequisites) {
            buffsTaken.TryGetValue(prereq, out int numberOfTimesPrereqTaken);
            hasAllPrereqs &= numberOfTimesPrereqTaken > 0;
        }

        if (!hasAllPrereqs) return 0;

        bool canAffordBuff = bank.Value >= buff.Cost;
        if (chooseOnlyAffordable && !canAffordBuff) return 0;

        return 1;
    }

    public void EndLevelUpSequence(Buff chosenBuff) {
        buffsTaken.TryGetValue(chosenBuff, out int numberOfTimesBuffTaken);
        buffsTaken[chosenBuff] = numberOfTimesBuffTaken + 1;

        levelUpBuff?.Apply();
        levelUpSequenceRunning = false;
        levelUpOptionPanel.gameObject.SetActive(false);
        Time.timeScale = 1;
    }
}

}
