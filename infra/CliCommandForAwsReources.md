\---------------------- RESOURCE GROUP --------------------------------

aws resource-groups create-group --name "oit-rg-keka-cw-integration" --resource-query "{\\"Type\\":\\"TAG\_FILTERS\_1\_0\\",\\"Query\\":\\"{\\\\\\"ResourceTypeFilters\\\\\\":\[\\\\\\"AWS::AllSupported\\\\\\"],\\\\\\"TagFilters\\\\\\":\[{\\\\\\"Key\\\\\\":\\\\\\"Project\\\\\\",\\\\\\"Values\\\\\\":\[\\\\\\"OitConnectWiseKekaIntegrationService\\\\\\"]}]}\\"}" --tags Project=OitConnectWiseKekaIntegrationService,Environment=uat,Name=oit-rg-keka-cw-integration --region ap-south-1



aws resource-groups delete-group --group-name "OIT-RG-KEKA-CW-INTEGRATION" --region ap-south-1



\---------------------- CLOUDWATCH LOGS --------------------------------



aws logs create-log-group --log-group-name "oit-logs-keka-cw-integration" --tags Project=OitConnectWiseKekaIntegrationService,Environment=uat,Name=oit-logs-keka-cw-integration --region ap-south-1



aws logs delete-log-group --log-group-name "/ecs/oit-keka-cw-integration" --region ap-south-1



aws logs put-retention-policy --log-group-name "oit-logs-keka-cw-integration" --retention-in-days 30 --region ap-south-1



\---------------------  PARAMETERS --------------------------------------



aws ssm put-parameter --name "oit-param-keka-apikey" --value "AogIfeJf7AmcUj7OUjDfS1AATE8CPG-cYY3U87Q8dgA=" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-keka-apikey --region ap-south-1



aws ssm put-parameter --name "oit-param-cw-apiversion" --value "/v4\_6\_release/apis/3.0" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-cw-apiversion --region ap-south-1



aws ssm put-parameter --name "oit-param-cw-baseurl" --value "https://na.myconnectwise.net" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-cw-baseurl --region ap-south-1



aws ssm put-parameter --name "oit-param-cw-clientid" --value "dddffe51-65c9-440f-93c7-92e68ae271f3" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-cw-clientid --region ap-south-1



aws ssm put-parameter --name "oit-param-cw-companyid" --value "oculusit" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-cw-companyid --region ap-south-1



aws ssm put-parameter --name "oit-param-keka-cw-pagesize" --value "200" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-keka-cw-pagesize --region ap-south-1



aws ssm put-parameter --name "oit-param-cw-privatekey" --value "wqiu3muwhxYMZHJc" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-cw-privatekey --region ap-south-1



aws ssm put-parameter --name "oit-param-cw-publickey" --value "CeiNfHfLVueyDBGQ" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-cw-publickey --region ap-south-1



aws ssm put-parameter --name "oit-param-keka-cw-tablename" --value "oit-keka-cw-statemanagement" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-keka-cw-tablename --region ap-south-1



aws ssm put-parameter --name "oit-param-keka-apibaseurl" --value "https://rajkumartezo.kekademo.com" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-keka-apibaseurl --region ap-south-1



aws ssm put-parameter --name "oit-param-keka-clientid" --value "f47f78dc-b384-4be6-ac72-0bc5130d67eb" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-keka-clientid --region ap-south-1



aws ssm put-parameter --name "oit-param-keka-clientsecret" --value "V7semS62OSQahqplMQv3" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-keka-clientsecret --region ap-south-1



aws ssm put-parameter --name "oit-param-keka-scope" --value "kekaapi" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-keka-scope --region ap-south-1



aws ssm put-parameter --name "oit-param-keka-granttype" --value "kekaapi" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-keka-granttype --region ap-south-1



aws ssm put-parameter --name "oit-param-keka-identityurl" --value "https://login.kekademo.com" --type "SecureString" --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-param-keka-identityurl --region ap-south-1



\----------------------------- DYNAMODB ---------------------------------------------------------------



aws dynamodb create-table --table-name oit-keka-cw-statemanagement --attribute-definitions AttributeName=syncType,AttributeType=S --key-schema AttributeName=syncType,KeyType=HASH --billing-mode PAY\_PER\_REQUEST --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-keka-cw-statemanagement --region ap-south-1



\------------------------------- ECR Repository ---------------------------------------------------------



aws ecr create-repository --repository-name oit-repo-keka-cw-integration --region ap-south-1 --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-repo-keka-cw-integration



&#x20;"repositoryUri": "504884946059.dkr.ecr.ap-south-1.amazonaws.com/oit-repo-keka-cw-integration"



\-------------------------  TASK ROLE and SCHEDULER ROLE ----------------------------------------------



**Role                        Used By                 Permissions**

oit-keka-cw-task-role       ECS Container       ECR, SSM, CloudWatch, DynamoDB

oit-keka-cw-scheduler-role  EventBridge            ECS FullAccess



ECS container needs to access ECR, SSM, CloudWatch and DynamoDB — but AWS does

not allow access without explicit permission.



IAM Roles = the permission system!

No username or password needed.

AWS handles it automatically.



**Two files Created -**



trust-policy-ecs.json

&#x20; → "ECS tasks are allowed to use this role"

&#x20; → Like writing "only ECS can use this badge"



trust-policy-scheduler.json

&#x20; → "EventBridge is allowed to use this role"

&#x20; → Like writing "only scheduler can use this pass"



**Why two separate roles?**



Security best practice — least privilege:

&#x20; ECS role    → Only what container needs

&#x20; Scheduler   → Only what scheduler needs



If one is compromised:

&#x20; → Damage is limited to its permissions only

&#x20; → Not everything! ✅



aws iam create-role --role-name oit-keka-cw-task-role --assume-role-policy-document file://trust-policy-ecs.json --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-keka-cw-task-role



**Attach Policies/permissions to the task role**



aws iam attach-role-policy --role-name oit-keka-cw-task-role --policy-arn arn:aws:iam::aws:policy/AmazonEC2ContainerRegistryReadOnly

aws iam attach-role-policy --role-name oit-keka-cw-task-role --policy-arn arn:aws:iam::aws:policy/AmazonSSMReadOnlyAccess

aws iam attach-role-policy --role-name oit-keka-cw-task-role --policy-arn arn:aws:iam::aws:policy/CloudWatchLogsFullAccess

aws iam attach-role-policy --role-name oit-keka-cw-task-role --policy-arn arn:aws:iam::aws:policy/AmazonDynamoDBFullAccess



**Create and attach permission to the Scheduler role**



aws iam create-role --role-name oit-keka-cw-scheduler-role --assume-role-policy-document file://trust-policy-scheduler.json --tags Key=Project,Value=OitConnectWiseKekaIntegrationService Key=Environment,Value=uat Key=Name,Value=oit-keka-cw-scheduler-role



aws iam attach-role-policy --role-name oit-keka-cw-scheduler-role --policy-arn arn:aws:iam::aws:policy/AmazonECS\_FullAccess



**Image Explanation -**



Left side   → Task Role flow

&#x20; ECS Fargate assumes oit-keka-cw-task-role

&#x20; → Grants access to ECR, SSM, CloudWatch, DynamoDB

&#x20; → Trust: ecs-tasks.amazonaws.com



Right side  → Scheduler Role flow

&#x20; EventBridge assumes oit-keka-cw-scheduler-role

&#x20; → Grants ECS FullAccess to launch tasks

&#x20; → Trust: scheduler.amazonaws.com



Bottom      → Simple analogy cards

&#x20; Task role     = Employee ID badge

&#x20; Scheduler role = Manager pass



\------------------  ECS CLUSTER --------------

aws ecs create-cluster --cluster-name oit-keka-cw-cluster --region ap-south-1 --tags key=Project,value=OitConnectWiseKekaIntegrationService key=Environment,value=uat key=Name,value=oit-keka-cw-cluster

