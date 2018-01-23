using AssetManagerPackage;
using Assets.Scripts;
using IntegratedAuthoringTool;
using IntegratedAuthoringTool.DTOs;
using RolePlayCharacter;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Utilities;
using WellFormedNames;
using System.Text.RegularExpressions;
using UnityEngine.SceneManagement;

public class ScenarioManager : MonoBehaviour
{
    [SerializeField]
    private string IATScenario;

    [SerializeField]
    private bool SaveLog;

    [SerializeField]
    private bool ResetEmotions;

    [SerializeField]
    private Text AgentUtterance;

    [SerializeField]
    private Text AgentInternalStateText;

    [SerializeField]
    private Button PreviousPageButton;

    [SerializeField]
    private Button NextPageButton;

    [SerializeField]
    private List<Button> DialogueButtons;

    [SerializeField]
    private Button RestartButton;

    private IntegratedAuthoringToolAsset iat;
    private RolePlayCharacterAsset agent1RPC;
    private RolePlayCharacterAsset agent2RPC;
    private RolePlayCharacterAsset playerRPC;

    private List<DialogueStateActionDTO> playerDialogues;
    private int currentPageNumber = 0;

    // Use this for initialization
    private void Start()
    {
        AssetManager.Instance.Bridge = new AssetManagerBridge();

        var streamingAssetsPath = Application.streamingAssetsPath;

#if UNITY_EDITOR || UNITY_STANDALONE
        streamingAssetsPath = "file://" + streamingAssetsPath;
#endif
        if(string.IsNullOrEmpty(IATScenario))
        {
            Debug.LogError("Please specify an IAT scenario file!");
            return;
        }

        iat = IntegratedAuthoringToolAsset.LoadFromFile(IATScenario);
        var characterSources = iat.GetAllCharacterSources().ToList();

        //AGENT 1
        agent1RPC = RolePlayCharacterAsset.LoadFromFile(characterSources[1].Source);
        agent1RPC.LoadAssociatedAssets();
        iat.BindToRegistry(agent1RPC.DynamicPropertiesRegistry);

        //AGENT 2
        agent2RPC = RolePlayCharacterAsset.LoadFromFile(characterSources[2].Source);
        agent2RPC.LoadAssociatedAssets();
        iat.BindToRegistry(agent2RPC.DynamicPropertiesRegistry);

        //PLAYER
        playerRPC = RolePlayCharacterAsset.LoadFromFile(characterSources[0].Source);
        playerRPC.LoadAssociatedAssets();
        iat.BindToRegistry(playerRPC.DynamicPropertiesRegistry);

        playerDialogues = DeterminePlayerDialogues();
        UpdatePlayerDialogOptions(true);

        AgentUtterance.text = String.Empty;

        StartCoroutine(UpdateEmotionalState(1f));
        StartCoroutine(DetermineAgentDialogue(0.2f));

        NextPageButton.onClick.RemoveAllListeners();
        NextPageButton.onClick.AddListener(() => OnNextPage());

        PreviousPageButton.onClick.RemoveAllListeners();
        PreviousPageButton.onClick.AddListener(() => OnPreviousPage());

        RestartButton.onClick.RemoveAllListeners();
        RestartButton.onClick.AddListener(() => RestartScene());
    }

    private List<DialogueStateActionDTO> DeterminePlayerDialogues()
    {
        var actions = playerRPC.Decide().ToArray();
        var dOpt = new List<DialogueStateActionDTO>();
        foreach (var action in actions)
        {
            if (action.Key.ToString().Equals(IATConsts.DIALOG_ACTION_KEY))
            {
                Name currentState = action.Parameters[0];
                Name nextState = action.Parameters[1];
                Name meaning = action.Parameters[2];
                Name style = action.Parameters[3];
                var dialogs = iat.GetDialogueActions(currentState, nextState, meaning, style);
                dOpt.AddRange(dialogs);
            }
        }
        dOpt = dOpt.Distinct().ToList();
        var additional = iat.GetDialogueActionsByState("Any");
        dOpt.AddRange(additional);
        return dOpt;
    }

    // Update is called once per frame
    private void Update()
    {
    }

    #region Coroutines
    private IEnumerator UpdateEmotionalState(float updateTime)
    {
        while (true)
        {
            var SIPlayerAgent = agent1RPC.GetBeliefValue("ToM(Player, SI(SELF))");
            var SIAgentPlayer = agent1RPC.GetBeliefValue("SI(Player)");

            if (!agent1RPC.GetAllActiveEmotions().Any())
            {
                this.AgentInternalStateText.text = "Mood: " + agent1RPC.Mood + ", Emotions: [], SI(A,P): " + SIAgentPlayer + " SI(P,A): " + SIPlayerAgent;
            }
            else
            {
                var aux = "Mood: " + agent1RPC.Mood + ", Emotions: [";

                StringBuilder builder = new StringBuilder();

                var query = agent1RPC.GetAllActiveEmotions().OrderByDescending(e => e.Intensity);

                foreach (var emt in query)
                {
                    builder.AppendFormat("{0}: {1:N2}, ", emt.Type, emt.Intensity);
                }
                aux += builder.Remove(builder.Length - 2, 2);
                this.AgentInternalStateText.text = aux + "], SI(A,P): " + SIAgentPlayer + " SI(P,A): " + SIPlayerAgent;
            }

            agent1RPC.Update();


            yield return new WaitForSeconds(updateTime);
        }
    }

    private IEnumerator DetermineAgentDialogue(float updateTime)
    {
        while (true)
        {
            var actions = agent1RPC.Decide().ToArray();
            var action = actions.Where(a => a.Key.ToString().Equals(IATConsts.DIALOG_ACTION_KEY)).FirstOrDefault();

            if (action != null)
            {
                Name cs = action.Parameters[0];
                Name ns = action.Parameters[1];
                Name m = action.Parameters[2];
                Name s = action.Parameters[3];
                var dialogs = iat.GetDialogueActions(cs, ns, m, s);
                var dialog = dialogs.Shuffle().FirstOrDefault();
                var processed = this.ReplaceVariablesInDialogue(dialog.Utterance);

                HandleSpeakAction(dialog.Id, agent1RPC.CharacterName.ToString(), IATConsts.PLAYER);
                AgentUtterance.text = processed;
          
            }
            yield return new WaitForSeconds(updateTime);
        }
    }
    #endregion

    //This method will replace every belief within [[ ]] by its value
    private string ReplaceVariablesInDialogue(string dialog)
    {
        var tokens = Regex.Split(dialog, @"\[|\]\]");

        var result = string.Empty;
        bool process = false;
        foreach (var t in tokens)
        {
            if (process)
            {
                var beliefValue = agent1RPC.GetBeliefValue(t);
                result += beliefValue;
                process = false;
            }
            else if (t == string.Empty)
            {
                process = true;
                continue;
            }
            else
            {
                result += t;
            }
        }
        return result;
    }

    private void UpdatePlayerDialogOptions(bool resetPageNumber)
    {

        if (resetPageNumber)
            currentPageNumber = 0;

        var pageSize = DialogueButtons.Count();

        this.UpdatePageButtons(playerDialogues.Count(), pageSize);

        var aux = currentPageNumber * pageSize;

        for (int i = 0; i < DialogueButtons.Count(); i++)
        {
            if (i + aux >= playerDialogues.Count())
            {
                DialogueButtons[i].gameObject.SetActive(false);
            }
            else
            {
                DialogueButtons[i].gameObject.SetActive(true);
                DialogueButtons[i].GetComponentInChildren<Text>().text = playerDialogues.ElementAt(i + aux).Utterance;
                var id = playerDialogues.ElementAt(i + aux).Id;
                DialogueButtons[i].onClick.RemoveAllListeners();
                DialogueButtons[i].onClick.AddListener(() => OnDialogueSelected(id));
            }
        }
    }

    private void UpdatePageButtons(int numOfOptions, int pageSize)
    {
        if (numOfOptions > pageSize * (currentPageNumber + 1))
        {
            this.NextPageButton.gameObject.SetActive(true);
        }
        else
        {
            this.NextPageButton.gameObject.SetActive(false);
        }

        if (currentPageNumber > 0)
        {
            this.PreviousPageButton.gameObject.SetActive(true);
        }
        else
        {
            this.PreviousPageButton.gameObject.SetActive(false);
        }
    }

    public void OnNextPage()
    {
        currentPageNumber++;
        UpdatePlayerDialogOptions(false);
    }

    public void OnPreviousPage()
    {
        currentPageNumber--;
        UpdatePlayerDialogOptions(false);
    }


    public void RestartScene()
    {
        StopAllCoroutines();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        return;
    }

    public void OnDialogueSelected(Guid dialogId)
    {
        /*var d =*/ iat.GetDialogActionById(dialogId);

        if (ResetEmotions)
        {
            var currentMood = agent1RPC.Mood;
            agent1RPC.ResetEmotionalState();
            agent1RPC.Mood = currentMood;
        }
        HandleSpeakAction(dialogId, "Player", agent1RPC.CharacterName.ToString());
    }

    private void HandleSpeakAction(Guid id, string subject, string target)
    {
        var d = iat.GetDialogActionById(id);

        var dAct = string.Format("Speak({0},{1},{2},{3})", d.CurrentState, d.NextState, d.Meaning, d.Style);

        agent1RPC.Perceive(EventHelper.ActionEnd(subject, dAct, target));
        playerRPC.Perceive(EventHelper.ActionEnd(subject, dAct, target));

        var dStatePropertyAgent = string.Format(IATConsts.DIALOGUE_STATE_PROPERTY, IATConsts.PLAYER);
        var dStatePropertyPlayer = string.Format(IATConsts.DIALOGUE_STATE_PROPERTY, agent1RPC.CharacterName.ToString());

        agent1RPC.Perceive(EventHelper.PropertyChange(dStatePropertyAgent, d.NextState, subject));
        playerRPC.Perceive(EventHelper.PropertyChange(dStatePropertyPlayer, d.NextState, subject));

        agent1RPC.Perceive(EventHelper.PropertyChange("Has(Floor)", target, subject));
        playerRPC.Perceive(EventHelper.PropertyChange("Has(Floor)", target, subject));

        if (SaveLog)
        {
            this.SaveState();
        }

        playerDialogues = DeterminePlayerDialogues();
        UpdatePlayerDialogOptions(true);
    }

    private void SaveState()
    {
        const string datePattern = "dd-MM-yyyy-H-mm-ss";
        agent1RPC.SaveToFile(Application.streamingAssetsPath
            + "\\Logs\\" + agent1RPC.CharacterName
            + "-" + DateTime.Now.ToString(datePattern) + ".rpc");
        playerRPC.SaveToFile(Application.streamingAssetsPath
            + "\\Logs\\" + playerRPC.CharacterName
            + "-" + DateTime.Now.ToString(datePattern) + ".rpc");
    }
}