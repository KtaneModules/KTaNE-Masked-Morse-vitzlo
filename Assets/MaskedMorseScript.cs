using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using KModkit;

public class MaskedMorseScript : MonoBehaviour {

    public KMBombInfo Bomb;
    public KMAudio Audio;
    public KMBombModule Module;

    public KMSelectable speedButtonUp, speedButtonDown;
    public KMSelectable[] gridButtons;
    public TextMesh speedText;
    public MeshRenderer[] gridColors, answerColors;
    public GameObject speedBase;
    public GameObject[] answerBases = new GameObject[3];
    public Coroutine coroutine;

    private readonly Dictionary<char, string> morse = new Dictionary<char, string> {
        {'A', ".-"}, {'B', "-..."}, {'C', "-.-."}, {'D', "-.."}, {'E', "."}, {'F', "..-."},
        {'G', "--."}, {'H', "...."}, {'I', ".."}, {'J', ".---"}, {'K', "-.-"}, {'L', ".-.."},
        {'M', "--"}, {'N', "-."}, {'O', "---"}, {'P', ".--."}, {'Q', "--.-"}, {'R', ".-."},
        {'S', "..."}, {'T', "-"}, {'U', "..-"}, {'V', "...-"}, {'W', ".--"}, {'X', "-..-"},
        {'Y', "-.--"}, {'Z', "--.."}, {'1', ".----"}, {'2', "..---"}, {'3', "...--"}, {'4', "....-"},
        {'5', "....."}, {'6', "-...."}, {'7', "--..."}, {'8', "---.."}, {'9', "----."}, {'0', "-----"}
    };

    private const string table = "LS3QJO2BKN49TMRAPIC1EX85FUZ6HW0DGVY7";

    private List<List<int>> lines = new List<List<int>>() {
            new List<int>() {0, 1, 2, 3, 4, 5}, new List<int>() {6, 7, 8, 9, 10, 11},
            new List<int>() {12, 13, 14, 15, 16, 17},
            new List<int>() {18, 19, 20, 21, 22, 23}, new List<int>() {24, 25, 26, 27, 28, 29},
            new List<int>() {30, 31, 32, 33, 34, 35},
            new List<int>() {0, 6, 12, 18, 24, 30}, new List<int>() {1, 7, 13, 19, 25, 31},
            new List<int>() {2, 8, 14, 20, 26, 32},
            new List<int>() {3, 9, 15, 21, 27, 33}, new List<int>() {4, 10, 16, 22, 28, 34},
            new List<int>() {5, 11, 17, 23, 29, 35},
        }, // possible rows/columns for the colors
        chosenLines = new List<List<int>>(); // the chosen row/columns for the colors

    private List<int> colorIndices = new List<int>(); // the order of the colors (rgb) in the table's line
    
    private int speed, offsetX, offsetY; // the current speed, the offsets between adjacent cells in the table's line
    private float[] speedList = {0, 1, 0.7f, 0.5f, 0.3f, 0.2f}; // the raw speeds, from 0 to 5
    private Color[] newColors = new Color[36]; // the array to transfer the updated colors to the materials
    private List<List<bool>> marqueeLights = new List<List<bool>>(); // where the colors are along their morse transmissions
    private int[] correctLine = new int[3]; // the "read"
    private List<int> correctPresses = new List<int>(); // the "answer"
    private List<bool> pressesList = new List<bool>() {false, false, false};

    static int moduleIdCounter = 1;
    int moduleId;
    private bool moduleSolved;

    void Awake () {
        moduleId = moduleIdCounter++; // version 1.0.0
    }

    void Start () {
        speedText.text = speed.ToString();
        StartCoroutine(MoveSpeedBase(true));
        for (int i = 0; i < 36; i++) gridButtons[i].OnInteract = GridPress(i);
        speedButtonUp.OnInteract = SpeedPress(true);
        speedButtonDown.OnInteract = SpeedPress(false);
        
        do {
            offsetX = UnityEngine.Random.Range(1, 7);
            offsetY = UnityEngine.Random.Range(1, 7);
        } while (!Coprime(offsetX, offsetY));

        string[] baseColors = {"red", "green", "blue"};
        int pointer = UnityEngine.Random.Range(0, 36);
        for (int i = 0; i < 3; i++) {
            correctLine[i] = pointer;
            int randomInsert = UnityEngine.Random.Range(0, marqueeLights.Count + 1);
            marqueeLights.Insert(randomInsert, MorseSquares(table[pointer]));
            colorIndices.Insert(randomInsert, i);
            pointer = Step(pointer);
        }
        for (int i = 0; i < 3; i++) {
            correctPresses.Add(pointer);
            pointer = Step(pointer);
        }

        for (int i = 0; i < 3; i++) {
            chosenLines.Add(lines.Shuffle()[0]);
            Debug.LogFormat("[Marquis Morse #{0}] The {1} character is {2} in {3} {4}.", moduleId, baseColors[i], table[correctLine[colorIndices[i]]],  
                chosenLines[i][1] - chosenLines[i][0] == 1 ? "row" : "column",
                chosenLines[i][1] - chosenLines[i][0] == 1 ? chosenLines[i][0] / 6 + 1 : chosenLines[i][0] + 1);
            lines.RemoveAt(0);

            int iHateBugs = marqueeLights[i].Count;
            for (int j = 0; j < iHateBugs; j++) marqueeLights[i].Add(marqueeLights[i][j]);
            
            List<bool> newLights = new List<bool>();
            int randomCut = UnityEngine.Random.Range(0, marqueeLights[i].Count);
            for (int j = randomCut; j < marqueeLights[i].Count; j++) newLights.Add(marqueeLights[i][j]);
            for (int j = 0; j < randomCut; j++) newLights.Add(marqueeLights[i][j]);
            marqueeLights[i] = newLights;
        }
        
        Debug.LogFormat("[Marquis Morse #{0}] The correct presses are {1}, {2}, and {3}.", moduleId,
            ToCoordinate(correctPresses[0]), ToCoordinate(correctPresses[1]), ToCoordinate(correctPresses[2]));
    }

    private void Solve() {
        Debug.LogFormat("[Marquis Morse #{0}] Module solved.", moduleId);
        Module.HandlePass();
        speed = 0;
        StopCoroutine(coroutine);
        ClearGrid();
        foreach (MeshRenderer mat in answerColors) mat.material.color = Color.green;
        StartCoroutine(MoveSpeedBase(false));
    }

    private IEnumerator Tick() {
        while (speed != 0) {
            UpdateGrid();
            yield return new WaitForSeconds(speedList[speed]);
        }
        yield return null;
    }

    private void UpdateGrid() {
        for (int i = 0; i < 36; i++) newColors[i] = Color.black;
        for (int i = 0; i < 3; i++) {
            marqueeLights[i].Add(marqueeLights[i][0]);
            marqueeLights[i].RemoveAt(0);
            
            for (int j = 0; j < 36; j++) {
                if (chosenLines[i].Contains(j) && marqueeLights[i][chosenLines[i].IndexOf(j)]) newColors[j] = AddColor(newColors[j], i);
                else if (!chosenLines[i].Contains(j) && UnityEngine.Random.Range(0, 6) == 0) newColors[j] = AddColor(newColors[j], i);
            }
        }
        for (int i = 0; i < 36; i++) gridColors[i].material.color = newColors[i];
        for (int i = 0; i < 3; i++) answerColors[i].material.color = newColors[correctPresses[i]];
    }

    private void ClearGrid() {
        for (int i = 0; i < 36; i++) gridColors[i].material.color = Color.black;
    }

    private List<bool> MorseSquares(char c) {
        List<bool> result = new List<bool>();
        foreach (char m in morse[c]) {
            result.AddRange(m == '-' ? new List<bool> {true, true, true, false} : new List<bool> {true, false});
        }
        result.AddRange(new List<bool> {false, false});
        return result;
    }

    private int Step(int pos) {
        pos += pos % 6 + offsetX >= 6 ? offsetX - 6 : offsetX;
        pos += pos / 6 + offsetY >= 6 ? 6 * (offsetY - 6) : 6 * offsetY;
        return pos;
    }

    private string ToCoordinate(int pos) {
        String headers = "ABCDEF123456";
        return "" + headers[pos % 6] + headers[pos / 6 + 6];
    }

    // takes the current color and returns the resulting color when a channel is added to it (0 = red, 1 = green, 2 = blue)
    private Color AddColor(Color current, int add) {
        List<Color> rgb = new List<Color> {Color.black, Color.blue, Color.green, Color.cyan, Color.red, Color.magenta, Color.yellow, Color.white};
        return rgb[rgb.IndexOf(current) + (int) Math.Pow(2, 2 - add)];
    }

    private bool Coprime(int a, int b) {
        for (int i = 2; i <= Math.Min(a, b); i++) if (a % i == 0 && b % i == 0) return false;
        return true;
    }

    // handles grid presses
    private KMSelectable.OnInteractHandler GridPress(int i) {
        return delegate {
            gridButtons[i].AddInteractionPunch(.1f);
            if (correctPresses.Contains(i) && !pressesList[correctPresses.IndexOf(i)]) {
                Debug.LogFormat("[Marquis Morse #{0}] {1} was pressed. That was correct.", moduleId, ToCoordinate(i));
                StartCoroutine(MoveAnswerBase(correctPresses.IndexOf(i)));
                pressesList[correctPresses.IndexOf(i)] = true;
                if (pressesList.All(x => x)) Solve();
            }
            else if (correctPresses.Contains(i)) {
                Debug.LogFormat("[Marquis Morse #{0}] {1} was pressed. That was already correct.", moduleId, ToCoordinate(i));
            }
            else {
                Debug.LogFormat("[Marquis Morse #{0}] {1} was pressed. That was incorrect.", moduleId, ToCoordinate(i));
                Module.HandleStrike();
            }
            return false;
        };
    }
    
    // handles up and down presses
    private KMSelectable.OnInteractHandler SpeedPress(bool up) {
        return delegate {
            (up ? speedButtonUp : speedButtonDown).AddInteractionPunch(.1f);
            speed = up ? Math.Min(5, speed + 1) : Math.Max(0, speed - 1);
            speedText.text = speed.ToString();
            if (speed == 0) {
                StopCoroutine(coroutine);
                ClearGrid();
            }
            if (speed == 1 && up) coroutine = StartCoroutine(Tick());
            return false;
        };
    }

    private IEnumerator MoveSpeedBase(bool slideOut) {
        float from = speedBase.transform.localPosition.x;
        float to = from + (slideOut ? -19.0f : 19.0f);
        float startTime = Time.fixedTime;
        float duration = 1.5f;
        do {
             speedBase.transform.localPosition = new Vector3(Easing.OutQuad(Time.fixedTime - startTime, from, to, duration),
                 speedBase.transform.localPosition.y, speedBase.transform.localPosition.z);
            yield return null;
        } while (Time.fixedTime < startTime + duration);
        speedBase.transform.localPosition = new Vector3(to, speedBase.transform.localPosition.y, speedBase.transform.localPosition.z);
    }
    
    private IEnumerator MoveAnswerBase(int index) {
        float from = answerBases[index].transform.localPosition.y;
        float to = from + 14.0f;
        float startTime = Time.fixedTime;
        float duration = 1.5f;
        do {
            answerBases[index].transform.localPosition = new Vector3(answerBases[index].transform.localPosition.x, Easing.OutQuad(Time.fixedTime - startTime, from, to, duration),
                answerBases[index].transform.localPosition.z);
            yield return null;
        } while (Time.fixedTime < startTime + duration);
        answerBases[index].transform.localPosition = new Vector3(answerBases[index].transform.localPosition.x, to, answerBases[index].transform.localPosition.z);
    }

    #pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Use <!{0} foobar> to do something.";
    #pragma warning restore 414

    IEnumerator ProcessTwitchCommand (string command)
    {
        command = command.Trim().ToUpperInvariant();
        List<string> parameters = command.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        yield return null;
    }

    IEnumerator TwitchHandleForcedSolve ()
    {
        yield return null;
    }
}
