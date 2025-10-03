using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class GeminiClient : IDisposable
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public string ApiKey { get; }
    private readonly List<string> _chatHistory = new List<string>();

    public GeminiClient(string apiKey)
    {
        ApiKey = apiKey?.Trim();

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ManilaServe-Chatbot/1.0");
        }
    }

    public async Task<string> GetChatResponseAsync(string prompt)
    {
        if (string.IsNullOrEmpty(ApiKey) || ApiKey == "YOUR_GEMINI_API_KEY_HERE")
        {
            return "API Key Required. Add your Gemini API key in the constructor.";
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Please type your question so I can help you.";
        }

        // Greeting
        string lowerPrompt = prompt.Trim().ToLower();
        if (lowerPrompt == "start" || lowerPrompt == "hello" || lowerPrompt == "hi")
        {
            return "KUMUSTA! MALIGAYANG PAGDATING SA MANILASERVE!\n\n" +
                   "Ako si ManilaServe, ang opisyal na virtual assistant ng Manila City Hall!\n\n" +
                   "PAANO AKO MAKAKATULONG SA INYO NGAYON?\n\n" +
                   "Mag-type lang ng tanong ninyo o sabihin kung anong kailangan ninyo!\n\n" +
                   "Mga halimbawa:\n" +
                   "- Saan ang Civil Registry?\n" +
                   "- Paano kumuha ng business permit?\n" +
                   "- Requirements para sa birth certificate?\n" +
                   "- Saan ang Ospital ng Maynila?\n" +
                   "- Contact number ng Mayor's office?\n" +
                   "- Tulong para sa senior citizens\n" +
                   "- Job opportunities sa PESO\n\n" +
                   "EMERGENCY CONTACTS:\n" +
                   "Manila Emergency: 117\n" +
                   "City Hall Main: (02) 8527-4000\n" +
                   "Manila Disaster Response: (02) 8527-4930\n\n" +
                   "ONLINE SERVICES:\n" +
                   "Website: manila.gov.ph\n" +
                   "Health Appointments: manilahealthdepartment.com\n\n" +
                   "Handa na akong tumulong! Ano ang inyong katanungan?";
        }

        _chatHistory.Add("User: " + prompt);
        if (_chatHistory.Count > 10)
        {
            _chatHistory.RemoveAt(0);
        }

        string context = BuildContext();
        context += "\n\nRecent conversation:\n" + string.Join("\n", _chatHistory.TakeLast(5));
        context += "\n\nUser Question: " + prompt + "\n\nManilaServe Response:";

        string url = string.Format("https://generativelanguage.googleapis.com/v1/models/gemini-2.5-flash-lite:generateContent?key={0}", ApiKey);

        // Manual JSON building (Unity compatible)
        string escapedContext = EscapeJsonString(context);
        string jsonContent = @"{
            ""contents"": [{
                ""parts"": [{
                    ""text"": """ + escapedContext + @"""
                }]
            }],
            ""generationConfig"": {
                ""temperature"": 0.7,
                ""topK"": 40,
                ""topP"": 0.95,
                ""maxOutputTokens"": 1024
            },
            ""safetySettings"": [
                {""category"": ""HARM_CATEGORY_HARASSMENT"", ""threshold"": ""BLOCK_MEDIUM_AND_ABOVE""},
                {""category"": ""HARM_CATEGORY_HATE_SPEECH"", ""threshold"": ""BLOCK_MEDIUM_AND_ABOVE""},
                {""category"": ""HARM_CATEGORY_SEXUALLY_EXPLICIT"", ""threshold"": ""BLOCK_MEDIUM_AND_ABOVE""},
                {""category"": ""HARM_CATEGORY_DANGEROUS_CONTENT"", ""threshold"": ""BLOCK_MEDIUM_AND_ABOVE""}
            ]
        }";

        try
        {
            using (var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json"))
            {
                using (var response = await _httpClient.PostAsync(url, httpContent))
                {
                    string responseContent = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        return string.Format("Connection Error: {0}\n{1}", response.StatusCode, responseContent);
                    }

                    // Parse JSON response using Unity's JsonUtility
                    string responseText = ParseGeminiResponse(responseContent);

                    if (!string.IsNullOrEmpty(responseText))
                    {
                        _chatHistory.Add("ManilaServe: " + responseText);
                        return responseText;
                    }

                    return "I couldn't generate a proper response. Please try again.";
                }
            }
        }
        catch (TaskCanceledException)
        {
            return "Request Timeout. Try again in a moment.";
        }
        catch (HttpRequestException ex)
        {
            return string.Format("Network Error: {0}", ex.Message);
        }
        catch (Exception ex)
        {
            return string.Format("Unexpected Error: {0}", ex.Message);
        }
    }

    private string EscapeJsonString(string str)
    {
        if (string.IsNullOrEmpty(str))
            return "";

        return str
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private string ParseGeminiResponse(string jsonResponse)
    {
        try
        {
            // Simple JSON parsing for Gemini response
            // Looking for "text": "content"
            int textIndex = jsonResponse.IndexOf("\"text\":");
            if (textIndex == -1)
                return "Unexpected response format.";

            int startQuote = jsonResponse.IndexOf("\"", textIndex + 7);
            if (startQuote == -1)
                return "Unexpected response format.";

            int endQuote = startQuote + 1;
            int escapeCount = 0;

            while (endQuote < jsonResponse.Length)
            {
                if (jsonResponse[endQuote] == '\\')
                {
                    escapeCount++;
                    endQuote++;
                    continue;
                }

                if (jsonResponse[endQuote] == '"' && escapeCount % 2 == 0)
                {
                    break;
                }

                escapeCount = 0;
                endQuote++;
            }

            if (endQuote >= jsonResponse.Length)
                return "Unexpected response format.";

            string text = jsonResponse.Substring(startQuote + 1, endQuote - startQuote - 1);

            // Unescape JSON string
            text = text
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");

            return text;
        }
        catch (Exception)
        {
            return "Error parsing response.";
        }
    }

    private string BuildContext()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("You are ManilaServe, the official virtual assistant for Manila City Hall.");
        sb.AppendLine();
        sb.AppendLine("CRITICAL INSTRUCTIONS:");
        sb.AppendLine("- You MUST ONLY use the information provided below to answer questions");
        sb.AppendLine("- DO NOT make up or guess any information");
        sb.AppendLine("- If the information is not in this context, say: For the most current information, please contact Manila City Hall at (02) 8527-4000");
        sb.AppendLine("- ALWAYS include room numbers, contact numbers, and names exactly as provided");
        sb.AppendLine();
        sb.AppendLine("MANILA CITY HALL DIRECTORY:");
        sb.AppendLine("Address: Manila City Hall Building, Padre Burgos Street, Ermita, Manila 1000");
        sb.AppendLine("Main Office: (02) 8527-4000");
        sb.AppendLine("Hours: Monday-Friday 8:00 AM - 5:00 PM");
        sb.AppendLine("Website: manila.gov.ph");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("OFFICE OF THE CITY MAYOR");
        sb.AppendLine("==============================");
        sb.AppendLine("Mayor: Hon. Francisco 'Isko Moreno' Domagoso");
        sb.AppendLine("Contact: (02) 8527-4991");
        sb.AppendLine("Room: 216 (2nd Floor)");
        sb.AppendLine("Chief of Staff: Mr. Cesar Chavez - Room 216, (02) 8527-0892");
        sb.AppendLine("Secretary to the Mayor: Manuel M. Zarcal - Room 215, (02) 8527-5191");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("CITY CIVIL REGISTRY OFFICE (CCRO)");
        sb.AppendLine("==============================");
        sb.AppendLine("Officer-In-Charge: Arsenio M. Riparip");
        sb.AppendLine("Contact: (02) 5308-9925");
        sb.AppendLine("Location: Near CCRO entrance, Ground Floor");
        sb.AppendLine("Hours: 8:00 AM - 5:00 PM (Mon-Fri)");
        sb.AppendLine();
        sb.AppendLine("VISION: To meet continuously changing demands through E-governance with global standards");
        sb.AppendLine("MISSION: Serve the public with highest standards through modern technology and trained human resources");
        sb.AppendLine();
        sb.AppendLine("SERVICES OFFERED:");
        sb.AppendLine("1. Issuance of Certified Copies:");
        sb.AppendLine("   - Birth Certificates");
        sb.AppendLine("   - Marriage Certificates");
        sb.AppendLine("   - Death Certificates");
        sb.AppendLine("2. Birth Registration (Live Birth)");
        sb.AppendLine("3. Marriage License Application");
        sb.AppendLine("4. Death Registration");
        sb.AppendLine("5. RA 9048 - Correction of Clerical Errors and Change of First Name");
        sb.AppendLine("6. RA 10172 - Correction of Day/Month in Date of Birth or Sex");
        sb.AppendLine("7. RA 9255 - Use of Father's Surname for Illegitimate Children");
        sb.AppendLine("8. Registration of Court Decrees (Annulment, Adoption, etc.)");
        sb.AppendLine();
        sb.AppendLine("GENERAL REQUIREMENTS:");
        sb.AppendLine("- Valid Government-issued ID");
        sb.AppendLine("- Application form (properly accomplished)");
        sb.AppendLine("- Payment of fees");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("MANILA DEPARTMENT OF SOCIAL WELFARE (MDSW)");
        sb.AppendLine("==============================");
        sb.AppendLine("Location: Room 108, Ground Floor, Manila City Hall");
        sb.AppendLine("Email: mdsw@manila.gov.ph");
        sb.AppendLine("Established: Created through Republic Act No. 4050 (June 18, 1964)");
        sb.AppendLine();
        sb.AppendLine("VISION: A city where disadvantaged and marginalized sectors are empowered for quality life");
        sb.AppendLine("MISSION: Achieve service excellence in poverty-alleviation and social protection programs");
        sb.AppendLine();
        sb.AppendLine("PROGRAMS & SERVICES:");
        sb.AppendLine("- Crisis Intervention for families in difficult situations");
        sb.AppendLine("- Assistance to Individuals in Crisis Situations (AICS)");
        sb.AppendLine("- Social welfare programs for vulnerable sectors");
        sb.AppendLine("- Community-based social protection programs");
        sb.AppendLine("- Livelihood and self-employment assistance");
        sb.AppendLine("- Medical assistance referrals");
        sb.AppendLine("- Burial assistance");
        sb.AppendLine("- Day Care Services (Early Childhood Care and Development)");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("OFFICE FOR SENIOR CITIZENS AFFAIRS (OSCA)");
        sb.AppendLine("==============================");
        sb.AppendLine("Officer-In-Charge: Ms. Elinor Jacinto");
        sb.AppendLine("Location: Room 115, Ground Floor, Manila City Hall");
        sb.AppendLine("Contact: (02) 8571-3878 / (02) 5310-3371 / (02) 5310-3372");
        sb.AppendLine("Email: osca@manila.gov.ph");
        sb.AppendLine();
        sb.AppendLine("VISION: Quality service to elderly and monitor compliance with RA 9994");
        sb.AppendLine("MISSION: Encourage active participation of senior citizens in city affairs");
        sb.AppendLine();
        sb.AppendLine("SENIOR CITIZEN BENEFITS (Under RA 9994 - Expanded Senior Citizens Act):");
        sb.AppendLine("Eligibility: Filipino citizens 60 years old and above");
        sb.AppendLine();
        sb.AppendLine("BENEFITS INCLUDE:");
        sb.AppendLine("- 20% discount on medicines (from all establishments)");
        sb.AppendLine("- 20% discount on medical and dental services");
        sb.AppendLine("- 20% discount on restaurants and hotels");
        sb.AppendLine("- 20% discount on transportation (land, sea, air)");
        sb.AppendLine("- 20% discount on basic necessities and prime commodities");
        sb.AppendLine("- VAT exemption on purchases");
        sb.AppendLine("- Priority lanes in government offices, banks, commercial establishments");
        sb.AppendLine("- Free medical and dental services in government facilities");
        sb.AppendLine("- Monthly social pension of P1,000 for indigent senior citizens (through DSWD)");
        sb.AppendLine("- Centenarian cash gift of P100,000 (one-time, from national government)");
        sb.AppendLine();
        sb.AppendLine("OSCA ID APPLICATION REQUIREMENTS:");
        sb.AppendLine("- Fully accomplished application form");
        sb.AppendLine("- Valid government-issued ID showing birthdate");
        sb.AppendLine("- Birth Certificate (1 photocopy)");
        sb.AppendLine("- Barangay Certificate of Residency");
        sb.AppendLine("- Recent 2x2 ID photo (colored, white background)");
        sb.AppendLine("- Proof of Manila residency");
        sb.AppendLine();
        sb.AppendLine("SERVICES PROVIDED:");
        sb.AppendLine("- OSCA ID issuance and renewal");
        sb.AppendLine("- Certificate of Registration");
        sb.AppendLine("- Certificate of No-Record");
        sb.AppendLine("- Certificate for burial documentation");
        sb.AppendLine("- Assistance with social pension applications");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("MANILA TRAFFIC & PARKING BUREAU (MTPB)");
        sb.AppendLine("==============================");
        sb.AppendLine("Officer-In-Charge: Mr. Dennis P. Viaje");
        sb.AppendLine("Contact: (02) 8527-9860 / 0932-662-2322");
        sb.AppendLine("Email: mtpbmanilacityhall@gmail.com");
        sb.AppendLine("Room: 350");
        sb.AppendLine("Facebook: @MTPBNoToKotong");
        sb.AppendLine();
        sb.AppendLine("MANDATE: Enforce traffic laws, designate parking areas, provide traffic assistance");
        sb.AppendLine("MISSION: Credible and unprejudiced traffic enforcement ensuring public safety");
        sb.AppendLine();
        sb.AppendLine("SERVICES OFFERED:");
        sb.AppendLine("1. Driver's License Redemption");
        sb.AppendLine("2. Vehicle Redemption (impounded vehicles)");
        sb.AppendLine("3. Traffic Violation Adjudication");
        sb.AppendLine("4. Overnight Parking Permits");
        sb.AppendLine("5. Tricycle Operation Permits");
        sb.AppendLine("6. Traffic Impact Clearance");
        sb.AppendLine("7. Traffic Advisories and Alerts");
        sb.AppendLine("8. Single Ticketing System (integrated with Metro Manila)");
        sb.AppendLine();
        sb.AppendLine("NOTE: Manila implements Single Ticketing System - violations can be paid in any Metro Manila city");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("MANILA DISASTER RISK REDUCTION & MANAGEMENT OFFICE (MDRRMO)");
        sb.AppendLine("==============================");
        sb.AppendLine("Officer-in-Charge: Nolen B. Andaya");
        sb.AppendLine("Contact: (02) 8527-4930");
        sb.AppendLine("Room: 326");
        sb.AppendLine("Emergency Hotline: 117");
        sb.AppendLine("Facebook: @sagipmanila");
        sb.AppendLine();
        sb.AppendLine("VISION: Effective and capable office for disaster-resilient city");
        sb.AppendLine("MISSION: Efficient disaster risk reduction programs promoting awareness and preparedness");
        sb.AppendLine();
        sb.AppendLine("CORE FUNCTIONS:");
        sb.AppendLine("- Lead agency for disaster resiliency (man-made and natural hazards)");
        sb.AppendLine("- Disaster preparedness training and seminars");
        sb.AppendLine("- Emergency response and rescue operations");
        sb.AppendLine("- Post-disaster recovery programs");
        sb.AppendLine("- Early warning systems and monitoring");
        sb.AppendLine("- Coordination with stakeholders (government, private, NGOs)");
        sb.AppendLine("- Community-based disaster risk reduction");
        sb.AppendLine();
        sb.AppendLine("DISASTER RESPONSE:");
        sb.AppendLine("- 24/7 Emergency Operations Center");
        sb.AppendLine("- Rapid response teams");
        sb.AppendLine("- Evacuation center management");
        sb.AppendLine("- Relief goods distribution");
        sb.AppendLine("- Search and rescue operations");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("PUBLIC EMPLOYMENT SERVICE OFFICE (PESO)");
        sb.AppendLine("==============================");
        sb.AppendLine("Head: Ofelia Domingo");
        sb.AppendLine("Location: 5th Floor, Manila City Hall");
        sb.AppendLine("Contact: +63 253102167");
        sb.AppendLine("Email: peso@manila.gov.ph");
        sb.AppendLine("Facebook: @pesocityofmanila");
        sb.AppendLine();
        sb.AppendLine("MANDATE: Non-fee charging multi-employment service facility (RA 8759 - PESO Act of 1999, amended by RA 10691)");
        sb.AppendLine();
        sb.AppendLine("SERVICES PROVIDED:");
        sb.AppendLine("1. JOB PLACEMENT SERVICES");
        sb.AppendLine("   - Local employment referral and placement");
        sb.AppendLine("   - Overseas employment assistance (with OWWA coordination)");
        sb.AppendLine("   - Job matching services");
        sb.AppendLine("   - Regular job fairs and recruitment events");
        sb.AppendLine();
        sb.AppendLine("2. CAREER DEVELOPMENT");
        sb.AppendLine("   - Career counseling and guidance");
        sb.AppendLine("   - Employment coaching");
        sb.AppendLine("   - Skills assessment");
        sb.AppendLine();
        sb.AppendLine("3. LABOR MARKET INFORMATION");
        sb.AppendLine("   - Employment trends and statistics");
        sb.AppendLine("   - Job vacancy listings");
        sb.AppendLine("   - Manpower registry and skills database");
        sb.AppendLine();
        sb.AppendLine("4. LIVELIHOOD & SELF-EMPLOYMENT");
        sb.AppendLine("   - Livelihood program assistance");
        sb.AppendLine("   - Self-employment facilitation");
        sb.AppendLine("   - Entrepreneurship information");
        sb.AppendLine();
        sb.AppendLine("5. SPECIAL ASSISTANCE");
        sb.AppendLine("   - OFW reintegration assistance");
        sb.AppendLine("   - Displaced worker support");
        sb.AppendLine("   - Services for PWDs and senior citizens");
        sb.AppendLine();
        sb.AppendLine("WHO CAN USE PESO:");
        sb.AppendLine("- Job seekers (unemployed, fresh graduates, career shifters)");
        sb.AppendLine("- Employers seeking workers");
        sb.AppendLine("- Returning OFWs");
        sb.AppendLine("- Students (career guidance)");
        sb.AppendLine("- Displaced workers");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("DEPARTMENT OF ENGINEERING & PUBLIC WORKS (DEPW)");
        sb.AppendLine("==============================");
        sb.AppendLine("City Engineer: Engr. Armando L. Andres");
        sb.AppendLine("Contact: (02) 8527-4924");
        sb.AppendLine("Email: depw@manila.gov.ph");
        sb.AppendLine("Room: 328-329");
        sb.AppendLine();
        sb.AppendLine("MANDATE: Deliver engineering services, building permit applications, and infrastructure projects");
        sb.AppendLine("Under: PD 1096 (National Building Code) and applicable laws");
        sb.AppendLine();
        sb.AppendLine("VISION: Provide stimulus for socio-economic development through efficient infrastructure management");
        sb.AppendLine("MISSION: Deliver quality infrastructure projects based on global technical standards");
        sb.AppendLine();
        sb.AppendLine("PRIMARY SERVICES:");
        sb.AppendLine();
        sb.AppendLine("1. BUILDING PERMIT PROCESSING");
        sb.AppendLine("   - New construction permits");
        sb.AppendLine("   - Renovation/alteration permits");
        sb.AppendLine("   - Demolition permits");
        sb.AppendLine("   - Occupancy permits");
        sb.AppendLine("   - Certificate of Completion");
        sb.AppendLine();
        sb.AppendLine("2. INSPECTORIAL SERVICES");
        sb.AppendLine("   - Building inspections (National Building Code - PD 1096)");
        sb.AppendLine("   - Electrical inspections (Philippine Electrical Code 2013)");
        sb.AppendLine("   - Plumbing inspections");
        sb.AppendLine("   - Fire safety compliance");
        sb.AppendLine("   - Occupancy inspections");
        sb.AppendLine();
        sb.AppendLine("3. INFRASTRUCTURE DEVELOPMENT");
        sb.AppendLine("   - Planning and design of city infrastructure");
        sb.AppendLine("   - Construction of roads and bridges");
        sb.AppendLine("   - Maintenance of government facilities");
        sb.AppendLine("   - Drainage and flood control projects");
        sb.AppendLine("   - Public works implementation");
        sb.AppendLine();
        sb.AppendLine("4. ENGINEERING SERVICES");
        sb.AppendLine("   - Engineering surveys and investigations");
        sb.AppendLine("   - Feasibility studies");
        sb.AppendLine("   - Project management");
        sb.AppendLine("   - Technical consultations");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("OTHER CITY OFFICES");
        sb.AppendLine("==============================");
        sb.AppendLine();
        sb.AppendLine("OFFICE OF THE CITY LEGAL OFFICER");
        sb.AppendLine("City Legal Officer: Atty. Luch Gempis");
        sb.AppendLine("Contact: (02) 8527-0912");
        sb.AppendLine("Room: 214");
        sb.AppendLine("Services: FREE LEGAL ASSISTANCE (Valid ID + Client info form required)");
        sb.AppendLine();
        sb.AppendLine("BUREAU OF PERMITS");
        sb.AppendLine("City Government Office Head: Levi C. Facundo");
        sb.AppendLine("Contact: (02) 5310-4184");
        sb.AppendLine("Room: 110");
        sb.AppendLine("Services: Business permits, special permits");
        sb.AppendLine("Requirements: DTI/SEC papers, Barangay clearance, Fire Safety Clearance, Sanitary Permit, Cedula, Valid ID");
        sb.AppendLine("Costs: Mayors Permit P2,500 (quarterly), Cedula P30-500+");
        sb.AppendLine();
        sb.AppendLine("REAL PROPERTY TAX & ASSESSMENT");
        sb.AppendLine("City Treasurer: Paul Vega, (02) 8527-5020, Room: 152");
        sb.AppendLine("City Assessor: Engr. Jose V. de Juan, (02) 8527-4918, Room: 204-205");
        sb.AppendLine();
        sb.AppendLine("MANILA HEALTH DEPARTMENT");
        sb.AppendLine("Medical Center Chief: Dr. Grace H. Padilla");
        sb.AppendLine("Contact: (02) 8527-4960");
        sb.AppendLine("Room: 101");
        sb.AppendLine("Services: Health Certificates, Sanitary Permits, Medical consultation");
        sb.AppendLine("Health Facilities: 51 health centers, 12 lying-in clinics");
        sb.AppendLine("Online Appointment: manilahealthdepartment.com");
        sb.AppendLine();
        sb.AppendLine("MANILA HOSPITALS:");
        sb.AppendLine();
        sb.AppendLine("OSPITAL NG MAYNILA MEDICAL CENTER (OMMC)");
        sb.AppendLine("Address: Corner Quirino Avenue and Roxas Boulevard, Malate");
        sb.AppendLine("Type: 300-bed tertiary training hospital");
        sb.AppendLine("Accreditation: DOH & PhilHealth accredited, ISO 9001:2015 certified");
        sb.AppendLine("Services: Mother-Baby Friendly Hospital, Emergency (24/7)");
        sb.AppendLine("Awards: DOH Hall of Fame Award, 3-star DOE Energy Efficiency Rating");
        sb.AppendLine("For Manila residents with priority admission");
        sb.AppendLine();
        sb.AppendLine("OSPITAL NG TONDO");
        sb.AppendLine("Address: Jose Abad Santos St., Tondo");
        sb.AppendLine("Type: 50-bed secondary hospital");
        sb.AppendLine("Services: Healthcare for Tondo residents");
        sb.AppendLine();
        sb.AppendLine("STA. ANA HOSPITAL");
        sb.AppendLine("Type: 200-bed Level II hospital");
        sb.AppendLine("Established: 2010");
        sb.AppendLine("Accreditation: DOH & PhilHealth accredited");
        sb.AppendLine("Services: Emergency Room (24/7), OPD (Mon-Fri 8AM-5PM)");
        sb.AppendLine("Laboratory & Diagnostic: Available 24/7");
        sb.AppendLine();
        sb.AppendLine("MANILA HEALTH DISTRICT OFFICES:");
        sb.AppendLine("District 1 - Dr. Armie G. Vianzon, dho1mhd@yahoo.com");
        sb.AppendLine("District 2 - Dr. Renato Soliven, renatosolivenmd@gmail.com");
        sb.AppendLine("District 3 - Dr. Romeo Cando, mhddistrict3@gmail.com");
        sb.AppendLine("District 4 - Dr. Jocelyn Denoga, jocelynbacanidenoga@gmail.com");
        sb.AppendLine("District 5 - Dr. Dolores T. Manese, doloresmanese@yahoo.com");
        sb.AppendLine("District 6 - Dr. David B. Pinto");
        sb.AppendLine();
        sb.AppendLine("CITY ADMINISTRATOR");
        sb.AppendLine("City Administrator: Atty. Eduardo Quintos XIV");
        sb.AppendLine("Contact: (02) 8521-7505 / (02) 8527-0984");
        sb.AppendLine("Room: 217");
        sb.AppendLine();
        sb.AppendLine("CITY BUDGET OFFICE");
        sb.AppendLine("City Budget Officer: Ms. Jorjette B. Aquino");
        sb.AppendLine("Contact: (02) 5302-6731");
        sb.AppendLine("Room: 200");
        sb.AppendLine();
        sb.AppendLine("CITY PLANNING & DEVELOPMENT OFFICE");
        sb.AppendLine("Officer-In-Charge: Jonathan R. Galorio");
        sb.AppendLine("Contact: (02) 5310-8285");
        sb.AppendLine("Room: 105");
        sb.AppendLine();
        sb.AppendLine("CITY PERSONNEL OFFICE");
        sb.AppendLine("Officer-In-Charge: Ms. Thelma L. Perez");
        sb.AppendLine("Contact: (02) 5310-5318");
        sb.AppendLine("Room: 102, 411");
        sb.AppendLine();
        sb.AppendLine("OFFICE OF THE CITY PROSECUTOR");
        sb.AppendLine("City Prosecutor: Atty. Giovanne T. Lim");
        sb.AppendLine("Contact: (02) 8527-8787");
        sb.AppendLine("Room: 208");
        sb.AppendLine();
        sb.AppendLine("DEPARTMENT OF TOURISM, CULTURE & ARTS - MANILA");
        sb.AppendLine("Officer-In-Charge: Ar. Ernesto M. Oliveros");
        sb.AppendLine("Contact: (02) 8527-0906");
        sb.AppendLine("Room: 568-569");
        sb.AppendLine();
        sb.AppendLine("MANILA PUBLIC INFORMATION OFFICE");
        sb.AppendLine("Officer-in-Charge: Mr. Mark Richmund M. de Leon");
        sb.AppendLine("Contact: (02) 5310-6529");
        sb.AppendLine("Email: publicinfo@manila.gov.ph");
        sb.AppendLine("Room: 563-564");
        sb.AppendLine();
        sb.AppendLine("MANILA CITY LIBRARY");
        sb.AppendLine("Contact: For schedule and services info");
        sb.AppendLine();
        sb.AppendLine("LOCAL BOARD OF ASSESSMENT APPEALS");
        sb.AppendLine("Officer-in-Charge: Atty. Jaime R. Tejero");
        sb.AppendLine("Contact: (02) 8405-0081");
        sb.AppendLine("Room: 117-119");
        sb.AppendLine();
        sb.AppendLine("PARKS DEVELOPMENT OFFICE");
        sb.AppendLine("Officer-In-Charge: Ms. Mylene C. Villanueva");
        sb.AppendLine("Contact: (02) 5310-2618 / (02) 5310-2573");
        sb.AppendLine();
        sb.AppendLine("MANILA EDUCATIONAL INSTITUTIONS:");
        sb.AppendLine("- Universidad de Manila");
        sb.AppendLine("- Pamantasan ng Lungsod ng Maynila (PLM)");
        sb.AppendLine("- Division of City Schools Manila");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("EMERGENCY CONTACTS");
        sb.AppendLine("==============================");
        sb.AppendLine("Manila Emergency: 117");
        sb.AppendLine("Fire Department: (02) 8527-4444");
        sb.AppendLine("Police: (02) 8527-4000");
        sb.AppendLine("Manila Disaster Risk Reduction: (02) 8527-4930");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("E-GOVERNMENT SERVICES");
        sb.AppendLine("==============================");
        sb.AppendLine("- COVID-19 Vaccine Registration");
        sb.AppendLine("- Safety Seal Certification");
        sb.AppendLine("- Online Health Center Appointments: manilahealthdepartment.com");
        sb.AppendLine("- City Ordinances Database: Available at Manila City Council website");
        sb.AppendLine("- Single Ticketing System: portal.singleticketing.com");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("GENERAL INFORMATION");
        sb.AppendLine("==============================");
        sb.AppendLine("- City has 897 barangays across 6 districts");
        sb.AppendLine("- Main website: manila.gov.ph");
        sb.AppendLine("- Public Information Office Email: publicinfo@manila.gov.ph");
        sb.AppendLine("- Manila is part of C40 Cities Climate Leadership Group");
        sb.AppendLine("- City Hall operates Monday-Friday, 8:00 AM - 5:00 PM");
        sb.AppendLine();
        sb.AppendLine("==============================");
        sb.AppendLine("RESPONSE RULES");
        sb.AppendLine("==============================");
        sb.AppendLine("- If user asks in TAGALOG: Respond in TAGALOG");
        sb.AppendLine("- If user asks in ENGLISH: Respond in ENGLISH");
        sb.AppendLine("- Always include: Location, Room number, Contact person, Phone number, Requirements (when applicable)");
        sb.AppendLine("- Provide complete information from this context");
        sb.AppendLine("- Be helpful, friendly, and professional");
        sb.AppendLine("- End with: May iba pa bang maitutulong ko? / Is there anything else I can help you with?");
        sb.AppendLine();
        sb.AppendLine("REMEMBER: ONLY use information from this context. DO NOT invent details.");

        return sb.ToString();
    }


    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}