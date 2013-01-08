using System;
using System.Collections.Generic;
using System.Text;
using RelayLib;
using GameTypes;
using System.Reflection;
using System.IO;

namespace GrimmLib
{
	/// <summary>
	/// Main responsibilities are to instantiate and update dialogue nodes.
	/// DialogueNodes also uses the OnSomeoneSaidSomething-event to send out Speech data.
	/// Expression- and Function delegates can be registered so that they are accessible from the scripts.
	/// </summary>
    public class DialogueRunner
    {
		public delegate void OnSomeoneSaidSomething(Speech pSpeech);
		public delegate bool Expression(string[] args);
		public delegate void Function(string[] args);
		public delegate void OnFocusConversation(string pConversation);
		public delegate void OnEvent(string pEventName);
		
		public Logger logger = new Logger();
		
		private TableTwo _dialogueTable;
		private Language _language;
        private List<DialogueNode> _dialogueNodes;
		private event OnSomeoneSaidSomething _onSomeoneSaidSomething;
		private Dictionary<string, Expression> _expressions = new Dictionary<string, Expression>();
		private Dictionary<string, Function> _functions = new Dictionary<string, Function>();
		private List<IRegisteredDialogueNode> _registeredDialogueNodes = new List<IRegisteredDialogueNode>();
		private event OnFocusConversation _onFocusConversation, _onDefocusConversation;
		private event OnEvent _onEvent;
		
        public DialogueRunner(RelayTwo pRelay, Language pLanguage)
        {
			D.isNull(pRelay);
			
			_dialogueTable = pRelay.GetTable(DialogueNode.TABLE_NAME);
            _language = pLanguage;
            _dialogueNodes = InstantiatorTwo.Process<DialogueNode>(_dialogueTable);
			foreach(DialogueNode n in _dialogueNodes) {
				n.SetRunner(this);
				if(n is IRegisteredDialogueNode) {
					IRegisteredDialogueNode ir = n as IRegisteredDialogueNode;
					_registeredDialogueNodes.Add(ir);
				}
			}
			RegisterBuiltInAPIExpressions();
        }
		
		public T Create<T>(string pConversation, Language pLanguage, string pName) where T : DialogueNode
		{
			T newDialogueNode = Activator.CreateInstance<T>();
			
			newDialogueNode.CreateNewRelayEntry(_dialogueTable, newDialogueNode.GetType().Name);
			newDialogueNode.conversation = pConversation;
			newDialogueNode.language = pLanguage;
			newDialogueNode.name = pName;
			newDialogueNode.SetRunner(this);
			_dialogueNodes.Add(newDialogueNode);
			if(newDialogueNode is ListeningDialogueNode) {
				IRegisteredDialogueNode ir = newDialogueNode as IRegisteredDialogueNode;
				_registeredDialogueNodes.Add(ir);
			}
			return newDialogueNode;
		}
		
        public void Update(float dt)
        {
            foreach (DialogueNode d in _dialogueNodes)
            {
				if(d.isOn) {
					d.Update(dt);
				}
			}
        }
		
		public DialogueNode GetDialogueNode(string pConversation, string pName) 
		{
			DialogueNode n = _dialogueNodes.Find(o => (o.language == _language && o.conversation == pConversation && o.name == pName));
			
			if(n != null) {
				return n;
			} else {
				DialogueNode ignoreLanguage = _dialogueNodes.Find(o => (o.conversation == pConversation && o.name == pName));
				if(ignoreLanguage != null) {
					throw new GrimmException("Can't find DialogueNode with name '" + pName + "' in conversation '" + pConversation + "'" + " when using language " + _language);
				} else {
					throw new GrimmException("Can't find DialogueNode with name '" + pName + "' in conversation '" + pConversation + "'");
				}
			}
		}

		/// <returns>
		/// Returns null if there is no active dialogue node in the conversation
		/// </returns>
		public DialogueNode GetActiveDialogueNode(string pConversation)
		{
			DialogueNode n = _dialogueNodes.Find(o => (o.language == _language && o.conversation == pConversation && o.isOn));
			return n;
		}
		
		public bool ConversationIsRunning(string pConversation)
		{
			return (_dialogueNodes.Find(o => (o.language == _language && o.conversation == pConversation && o.isOn)) != null);
		}
		
		/// <summary>
		/// Looks in a conversation for a ConversationStartDialogueNode
		/// </summary>
		public void StartConversation(string pConversation)
		{
			DialogueNode conversationStartNode = 
				_dialogueNodes.Find(o => (o.language == _language && o.conversation == pConversation && o is ConversationStartDialogueNode));
			
			if(conversationStartNode != null) {
				logger.Log("Starting conversation '" + pConversation + "'");
				conversationStartNode.Start();
			}
			else {
				if(HasConversation(pConversation)) {
					throw new GrimmException("Can't find a ConversationStartDialogueNode in conversations '" + pConversation + "'");
				}
				else {
					throw new GrimmException("The dialogue runner doesn't contain the conversation '" + pConversation + "'");
				}
			}
		}
		
		public void StopConversation(string pConverstation)
		{
			logger.Log("Stopping conversation '" + pConverstation + "'");
			foreach(DialogueNode n in _dialogueNodes) {
				if(n.isOn && n.conversation == pConverstation) {
					n.Stop();
				}
			}
			foreach(IRegisteredDialogueNode i in _registeredDialogueNodes.ToArray()) {
				if(i.conversation == pConverstation) {
					_registeredDialogueNodes.Remove(i);
				}
			}
		}
		
		public void RemoveConversation(string pConversation)
		{
			foreach (DialogueNode d in _dialogueNodes.ToArray())
            {
				if(d.conversation == pConversation) {
					_dialogueNodes.Remove(d);
					if(d is IRegisteredDialogueNode) {
						IRegisteredDialogueNode ir = d as IRegisteredDialogueNode;
						_registeredDialogueNodes.Remove(ir);
					}
					_dialogueTable.RemoveRowAt(d.objectId);
				}
			}
		}
		
		public bool HasConversation(string pConversation)
		{
			int nrOfNodesForConversation = 0;
			foreach(DialogueNode n in _dialogueNodes)
			{
				if(n.conversation == pConversation)
				{
					nrOfNodesForConversation++;
				}
			}
			return (nrOfNodesForConversation > 0);
		}
		
		public void AddOnSomeoneSaidSomethingListener(OnSomeoneSaidSomething pOnSomeoneSaidSomething)
		{
			_onSomeoneSaidSomething += pOnSomeoneSaidSomething;
		}
		
		public void RemoveOnSomeoneSaidSomethingListener(OnSomeoneSaidSomething pOnSomeoneSaidSomething)
		{
			_onSomeoneSaidSomething -= pOnSomeoneSaidSomething;
		}
		
		internal void SomeoneSaidSomething(Speech pSpeech)
		{
			D.isNull(pSpeech);
			if(_onSomeoneSaidSomething != null) {
				_onSomeoneSaidSomething(pSpeech);
			}
			else {
				logger.Log("No listeners to dialogue runner");
			}
		}
		
		public void AddExpression(string pName, Expression pExpression)
		{
			_expressions[pName] = pExpression;
		}
		
		public bool HasExpression(string pName)
		{
			return _expressions.ContainsKey(pName);
		}
		
		public bool EvaluateExpression(string pExpressionName, string[] args)
		{
#if DEBUG
			if(!_expressions.ContainsKey(pExpressionName)) {
				throw new GrimmException("Can't find expression '" + pExpressionName + "' in Dialogue Runner");
			}
#endif
			Expression e = _expressions[pExpressionName];
			bool result = e(args);
			if(result) {
				//logger.Log("Result of expression '" + pExpressionName + "' was true!");
			} else {
				//logger.Log("Result of expression '" + pExpressionName + "' was false...");
			}
			return result;
		}
		
		public string GetExpressionsAsString()
		{
			string[] keys = new string[_expressions.Count];
			int i = 0;
			foreach(string s in _expressions.Keys) {
				keys[i++] = s;
			}
			return string.Join(", ", keys);
		}
	
		public void AddFunction(string pName, Function pFunction)
		{
			_functions[pName] = pFunction;
		}
		
		public bool HasFunction(string pName)
		{
			return _functions.ContainsKey(pName);
		}

		public void CallFunction(string pFunctionName, string[] args)
		{
#if DEBUG
			if(!_functions.ContainsKey(pFunctionName)) {
				throw new GrimmException("Can't find function '" + pFunctionName + "' in Dialogue Runner");
			}
#endif
			Function f = _functions[pFunctionName];
			f(args);
		}
			
		public string GetFunctionsAsString()
		{
			string[] keys = new string[_functions.Count];
			int i = 0;
			foreach(string s in _functions.Keys) {
				keys[i++] = s;
			}
			return string.Join(", ", keys);
		}
		
		public void RunStringAsFunction(string pCommand)
		{
			const string conversation = "RunStringAsFunction";
			
			RemoveConversation(conversation);
			DialogueScriptLoader d = new DialogueScriptLoader(this);
			d.CreateDialogueNodesFromString(pCommand, conversation);
			StartConversation(conversation);
		}

		public void AddOnEventListener(OnEvent pOnEvent)
		{
			_onEvent += pOnEvent;
		}

		public void RemoveOnEventListener(OnEvent pOnEvent)
		{
			_onEvent -= pOnEvent;
		}
		
		public void EventHappened(string pEventName)
		{
			logger.Log("Event [" + pEventName + "]");
			foreach(IRegisteredDialogueNode l in _registeredDialogueNodes.ToArray())
			{
				ListeningDialogueNode listeningNode = l as ListeningDialogueNode;
				if(listeningNode != null && l.isListening && listeningNode.eventName == pEventName) {
					listeningNode.EventHappened();
				}
			}

			if(_onEvent != null) {
				_onEvent(pEventName);
			}
		}
		
		public void ConversationEnded(string pConversation)
		{
			logger.Log("Conversation '" + pConversation + "' ended");
			foreach(IRegisteredDialogueNode l in _registeredDialogueNodes)
			{
				if(l.conversation == pConversation) {
					l.isListening = false;
				}
			}
		}
		
		public void ScopeEnded(string pConversation, string pScopeNode)
		{
			logger.Log("Scope '" + pScopeNode + "' for conversation '" + pConversation + "' ended");
			foreach(ListeningDialogueNode l in _registeredDialogueNodes)
			{
				if(l.conversation == pConversation && l.scopeNode == pScopeNode) {
					l.isListening = false;
				}
			}
		}
		
		public void CancelRegisteredNode(string pConversation, string pListenerHandle)
		{
			foreach(IRegisteredDialogueNode l in _registeredDialogueNodes)
			{
				if(l.conversation == pConversation && l.handle == pListenerHandle) {
					logger.Log("Cancelled node " + l.name);
					l.isListening = false;
				}
			}
		}
		
		public void AddFocusConversationListener(OnFocusConversation pOnFocusConversation)
		{
			_onFocusConversation += pOnFocusConversation;
		}
		
		public void RemoveFocusConversationListener(OnFocusConversation pOnFocusConversation)
		{
			_onFocusConversation -= pOnFocusConversation;
		}
		
		public void AddDefocusConversationListener(OnFocusConversation pOnDefocusConversation)
		{
			_onDefocusConversation += pOnDefocusConversation;
		}
		
		public void RemoveDefocusConversationListener(OnFocusConversation pOnDefocusConversation)
		{
			_onDefocusConversation -= pOnDefocusConversation;
		}
		
		/// <summary>
		/// The active conversation is the conversation that the player currently is engeged in, and doing input to
		/// </summary>
		public void FocusConversation(string pConversation)
		{
			if(_onFocusConversation != null) {
				_onFocusConversation(pConversation);
			}
			else {
				throw new GrimmException("Trying to focus conversation " + pConversation + " but there is no onFocusConversation listener.");
			}
		}
		
		public void DefocusConversation(string pConversation)
		{
			if(_onDefocusConversation != null) {
				_onDefocusConversation(pConversation);
			}
			else {
				throw new GrimmException("Trying to defocus conversation " + pConversation + " but there is no onDefocusConversation listener.");
			}
		}
		
		public Language language {
			set {
				_language = value;
			}
		}
		
		public override string ToString()
		{
			return string.Format("DialogueRunner ({0} dialogue nodes, {1} registered dialogue nodes)", _dialogueNodes.Count, _registeredDialogueNodes.Count);
		}

		private void RegisterBuiltInAPIExpressions()
		{
			AddExpression("IsActive", IsActive);
		}
		
		// IsActive(string conversation)
		private bool IsActive(string[] args)
		{
			return ConversationIsRunning(args[0]);
		}
    }    
}

