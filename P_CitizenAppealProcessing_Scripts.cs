using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EleWise.ELMA.API;
using EleWise.ELMA.Model.Common;
using EleWise.ELMA.Model.Entities;
using EleWise.ELMA.Model.Managers;
using EleWise.ELMA.Model.Types.Settings;
using EleWise.ELMA.Model.Entities.ProcessContext;
using Context = EleWise.ELMA.Model.Entities.ProcessContext.P_CitizenAppealProcessing;
using EleWise.ELMA.ConfigurationModel;
using EleWise.ELMA.Messaging.Email;
using System.Net;
using System.IO;

namespace EleWise.ELMA.Model.Scripts
{
	/// <summary>
	/// Модуль сценариев процесса "Обработка обращений жителей"
	/// </summary>
	/// <example> 
	/// <![CDATA[
	/// >>>>>>>>>>>>>>>ВАЖНАЯ ИНФОРМАЦИЯ!!!<<<<<<<<<<<<<<<
	/// Данный редактор создан для работы с PublicAPI. 
	/// PublicAPI предназначен для разработки сценариев ELMA.
	/// Например, с помощью PublicAPI можно добавить комментарий к документу:
	/// //Загружаем документ
	/// var doc = PublicAPI.Docflow.Document.Load(56);
	/// //Добавляем комментарий
	/// PublicAPI.Docflow.Document.AddComment(doc, "тут ваш комментарий");
	/// 
	/// Более подробно про PublicAPI вы можете узнать тут: http://www.elma-bpm.ru/kb/article-642ApiRoot.html
	/// 
	/// Если же вам нужна более серьёзная разработка, выходящая за рамки PublicAPI, используйте
	/// сторонние редакторы кода, такие как SharpDevelop и VisualStudio.
	/// Информацию по запуску кода в стороннем редакторе вы можете найти тут:
	/// http://www.elma-bpm.ru/kb/article-837.html
	/// ]]>
	/// </example>
	public partial class P_CitizenAppealProcessing_Scripts : EleWise.ELMA.Workflow.Scripts.ProcessScriptBase<Context>
	{
		/// <summary>
		/// CreateAppeal
		/// </summary>
		/// <param name="context">Контекст процесса</param>
		public virtual void CreateAppeal (Context context)
		{
			var address = PublicAPI.Objects.UserObjects.UserhcsAppealAddress.Filter ().Name (context.CitizenAddressString).Find ();
			var appeal = PublicAPI.Objects.UserObjects.UserhcsAppeal.Create ();
			appeal.AppealDate = DateTime.Now;
			appeal.CitizenName = context.CitizenName;
			appeal.CitizenEmail = context.CitizenEmail;
			appeal.CitizenAddressString = context.CitizenAddressString;
			appeal.AppealText = context.AppealText;
			if (address != null && address.Any ()) {
				appeal.Address = address.FirstOrDefault ();
			}
			appeal.Save ();
			//добавить вставку адреса нормального
			var AppealNameTemplate = @"Обращение №{0} - {1} | {2}";
			appeal.Name = string.Format (AppealNameTemplate, appeal.Id.ToString (), appeal.CitizenName, appeal.AppealDate.ToString ());
			context.Appeal = appeal;
		}

		/// <summary>
		/// GetCategory
		/// </summary>
		/// <param name="context">Контекст процесса</param>
		public virtual void GetCategory (Context context)
		{
            string categoryMagicUrl = @"http://192.168.137.146:4000/{0}";
            string methodName = @"";//get_category";

            HttpWebRequest request = WebRequest.Create(string.Format(categoryMagicUrl, methodName)) as HttpWebRequest;
            request.Method = "POST";
            //taskReq.Headers.Add("AuthToken", authToken);
            //taskReq.Headers.Add("SessionToken", sessionToken);
            request.Timeout = 10000;
            request.ContentType = "application/json; charset=utf-8";
            
            string body = context.Appeal.AppealText;
            byte[] bodyBytes = UTF8Encoding.Default.GetBytes(body);
            Stream requestStream = request.GetRequestStream();
            requestStream.Write(bodyBytes, 0, bodyBytes.Length);

            var response = request.GetResponse() as HttpWebResponse;
            var responseStream = response.GetResponseStream();
            var streamReader = new StreamReader(responseStream, Encoding.UTF8);
            var responseText = streamReader.ReadToEnd();
            context.AppealText = responseText;
			
			var categoryCode = responseText;//context.Category.Code;
			var categories = PublicAPI.Objects.UserObjects.UserhcsAppealCategory.Filter ().Code (categoryCode).Find ();
			if (categories != null && categories.Any ()) {
				context.Appeal.Category = categories.FirstOrDefault ();
				context.Appeal.Save ();
				if (context.Appeal.Category != null) {
					hcsAutoAnswer autoAnswer = null;
					if (context.Appeal.Category.AutoAnsweringPossible) {
						var autoAnswers = PublicAPI.Objects.UserObjects.UserhcsAutoAnswer.Filter ().Addresses (context.Appeal.Address).Categories (context.Appeal.Category).StartDate (new Ranges.DateTimeRange (null, context.Appeal.AppealDate)).EndDate (new Ranges.DateTimeRange (context.Appeal.AppealDate, null)).Find ();
						if (autoAnswers != null && autoAnswers.Any ()) {
							//сохранить автоответ в заявке
							autoAnswer = autoAnswers.FirstOrDefault ();
							context.AutoAnswer = autoAnswer;
						}
					}
					if (autoAnswer == null) {
						hcsAppealResponsible responsible = null;
						var responsibles = PublicAPI.Objects.UserObjects.UserhcsAppealResponsible.Filter ().Addresses (context.Appeal.Address).Categories (context.Appeal.Category).Find ();
						if (responsibles != null && responsibles.Any ()) {
							responsible = responsibles.FirstOrDefault ();
						}
					}
				}
			}
		}

		private void sendMail (string address, string subject, string body)
		{
			//EleWise.ELMA.Messaging.Email.
			var message = new System.Net.Mail.MailMessage (@"Clever TMN <clever.TMN@yandex.ru>", address);
			message.Subject = subject;
			message.Body = body;
			PublicAPI.Services.Email.SendMessage (message, null, null);
		}

		/// <summary>
		/// SendAutoAnswer
		/// </summary>
		/// <param name="context">Контекст процесса</param>
		public virtual void SendAutoAnswer (Context context)
		{
			var subject = string.Format ("Ответ на обращение №{0}", context.Appeal.Id.ToString ());
			var body = context.AutoAnswer != null ? context.AutoAnswer.ResponceText : "нет ответа";
			sendMail (context.Appeal.CitizenEmail, subject, body);
		}

		/// <summary>
		/// SendStartWork
		/// </summary>
		/// <param name="context">Контекст процесса</param>
		public virtual void SendStartWork (Context context)
		{
			var subject = string.Format ("Обращение №{0} принято в работу", context.Appeal.Id.ToString ());
			var body = "Оращение принято вработу в работу";
			sendMail (context.Appeal.CitizenEmail, subject, body);
		}

		/// <summary>
		/// ResponsibleDefine
		/// </summary>
		/// <param name="context">Контекст процесса</param>
		public virtual void ResponsibleDefine (Context context)
		{
		}

		/// <summary>
		/// ExecutorDefine
		/// </summary>
		/// <param name="context">Контекст процесса</param>
		public virtual void ExecutorDefine (Context context)
		{
			context.Executor = context.AppealResponsible.Users.FirstOrDefault ();
		}

		/// <summary>
		/// isResponsibleDefined
		/// </summary>
		/// <param name="context">Контекст процесса</param>
		/// <param name="GatewayVar"></param>
		public virtual bool isResponsibleNotDefined (Context context, object GatewayVar)
		{
			if (context.Appeal != null && context.Appeal.Category != null && context.Appeal.Address != null) {
				var responsibles = PublicAPI.Objects.UserObjects.UserhcsAppealResponsible.Filter ().Categories (context.Appeal.Category).Addresses (context.Appeal.Address).Find ();
				if (responsibles != null && responsibles.Any ()) {
					context.AppealResponsible = responsibles.FirstOrDefault ();
					return false;
				}
			}
			//context.AppealResponsible = 
			return true;
		}

		/// <summary>
		/// SendResponce
		/// </summary>
		/// <param name="context">Контекст процесса</param>
		public virtual void SendResponce (Context context)
		{
			var subject = string.Format ("Ответ на обращение №{0}", context.Appeal.Id.ToString ());
			var body = context.AppealResponce;
			sendMail (context.Appeal.CitizenEmail, subject, body);
		}
	}
}
