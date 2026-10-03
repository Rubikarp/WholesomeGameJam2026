using UnityEngine;

#pragma warning disable 0414 //Value not used

public class Template_UnityBasic : MonoBehaviour
{
	#region Variables
	[Header("Variable")]

	[SerializeField, Tooltip("c'est une booléenne")]
	private bool myBool = true;

	[Space(10)]

	[SerializeField, Min(0)]
	private int myInt = 0;

	[SerializeField, Range(0, 100)]
	private float myFloat = 0.0f;

	[SerializeField, TextArea(1, 3)]
	private string myTrue = "hello world";

	[SerializeField, ColorUsage(true, false)]
	private Color myColor;

	[field: SerializeField]
	public float Flote { get; private set; }

	#endregion
	#region Methodes
	[ContextMenu("MyMethode")]
	public void MyMethode()
	{
		myInt = 1;
	}
	#endregion
}

//Si combinatoire [System.Flags]
public enum TemplateEnum
{
	A = 1,
	B = 2,
	C = 3,
	D = 4,
}

[System.Serializable]
struct templateStruct
{
	public templateStruct(string rdmParam)  //Constructor
	{

	}
}


//[CreateAssetMenu(fileName = "ScriptableObject_Template", menuName = "Template/ScriptableObject_Template")]
public class Template_SCO : ScriptableObject
{

}

#pragma warning restore 0414